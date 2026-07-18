package plaid

import (
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"testing"

	plaidSDK "github.com/plaid/plaid-go/v20/plaid"
)

// newTestClient builds a client pointed at a local httptest server instead
// of Plaid's real API, so SyncTransactions's pagination/retry logic can be
// exercised without network access.
func newTestClient(server *httptest.Server) *client {
	cfg := plaidSDK.NewConfiguration()
	cfg.Servers = plaidSDK.ServerConfigurations{{URL: server.URL}}
	return &client{api: plaidSDK.NewAPIClient(cfg)}
}

func syncTxJSON(id string) string {
	return fmt.Sprintf(`{
		"transaction_id": %q,
		"account_id": "acct-1",
		"amount": 12.34,
		"date": "2026-07-01",
		"name": "Test Merchant",
		"pending": false,
		"payment_channel": "other"
	}`, id)
}

func TestSyncTransactions_FollowsHasMoreAcrossPages(t *testing.T) {
	var receivedCursors []string
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		var body struct {
			Cursor string `json:"cursor"`
		}
		_ = json.NewDecoder(r.Body).Decode(&body)
		receivedCursors = append(receivedCursors, body.Cursor)

		w.Header().Set("Content-Type", "application/json")
		switch len(receivedCursors) {
		case 1:
			fmt.Fprintf(w, `{"added":[%s],"modified":[],"removed":[],"next_cursor":"page2","has_more":true,"request_id":"r1"}`, syncTxJSON("tx-1"))
		case 2:
			fmt.Fprintf(w, `{"added":[%s],"modified":[],"removed":[],"next_cursor":"page3-final","has_more":false,"request_id":"r2"}`, syncTxJSON("tx-2"))
		default:
			t.Fatalf("unexpected extra request, cursor=%q", body.Cursor)
		}
	}))
	defer server.Close()

	c := newTestClient(server)
	added, _, _, nextCursor, err := c.SyncTransactions(t.Context(), "access-token", "")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(added) != 2 {
		t.Fatalf("added = %d transactions, want 2 (one per page)", len(added))
	}
	if nextCursor != "page3-final" {
		t.Fatalf("nextCursor = %q, want the cursor from the has_more=false page, not an intermediate one", nextCursor)
	}
	if len(receivedCursors) != 2 {
		t.Fatalf("made %d requests, want 2 (one per page)", len(receivedCursors))
	}
	if receivedCursors[1] != "page2" {
		t.Fatalf("second request cursor = %q, want the intermediate page's next_cursor", receivedCursors[1])
	}
}

func TestSyncTransactions_RestartsOnMutationDuringPagination(t *testing.T) {
	var calls int
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		var body struct {
			Cursor string `json:"cursor"`
		}
		_ = json.NewDecoder(r.Body).Decode(&body)
		calls++

		w.Header().Set("Content-Type", "application/json")
		switch calls {
		case 1:
			// First page of the first attempt.
			fmt.Fprintf(w, `{"added":[%s],"modified":[],"removed":[],"next_cursor":"page2","has_more":true,"request_id":"r1"}`, syncTxJSON("tx-1"))
		case 2:
			// Second page fails: data mutated mid-pagination.
			w.WriteHeader(http.StatusBadRequest)
			fmt.Fprint(w, `{"error_type":"TRANSACTIONS_ERROR","error_code":"TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION","error_message":"Underlying transaction data changed since last page was fetched. Please restart pagination from last update.","display_message":null,"request_id":"r2"}`)
		case 3:
			// Restart: back to the original starting cursor (empty), single page this time.
			if body.Cursor != "" {
				t.Errorf("restart request cursor = %q, want empty (the original starting cursor)", body.Cursor)
			}
			fmt.Fprintf(w, `{"added":[%s],"modified":[],"removed":[],"next_cursor":"final","has_more":false,"request_id":"r3"}`, syncTxJSON("tx-1-retry"))
		default:
			t.Fatalf("unexpected extra request #%d", calls)
		}
	}))
	defer server.Close()

	c := newTestClient(server)
	added, _, _, nextCursor, err := c.SyncTransactions(t.Context(), "access-token", "")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	// Only the restarted attempt's results should be returned — the
	// discarded first attempt's partial page must not leak through.
	if len(added) != 1 || added[0].PlaidID != "tx-1-retry" {
		t.Fatalf("added = %+v, want exactly the restarted attempt's single transaction", added)
	}
	if nextCursor != "final" {
		t.Fatalf("nextCursor = %q, want %q", nextCursor, "final")
	}
	if calls != 3 {
		t.Fatalf("calls = %d, want 3 (2 for the aborted attempt + 1 restart)", calls)
	}
}

func TestSyncTransactions_GivesUpAfterMaxRestarts(t *testing.T) {
	var calls int
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		calls++
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusBadRequest)
		fmt.Fprint(w, `{"error_type":"TRANSACTIONS_ERROR","error_code":"TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION","error_message":"mutated","display_message":null,"request_id":"r"}`)
	}))
	defer server.Close()

	c := newTestClient(server)
	_, _, _, _, err := c.SyncTransactions(t.Context(), "access-token", "")
	if err == nil {
		t.Fatal("expected an error after exhausting restarts, got nil")
	}
	if calls != maxSyncPaginationRestarts+1 {
		t.Fatalf("calls = %d, want %d (1 initial + %d restarts)", calls, maxSyncPaginationRestarts+1, maxSyncPaginationRestarts)
	}
}

func TestSyncTransactions_NonMutationErrorDoesNotRestart(t *testing.T) {
	var calls int
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		calls++
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusBadRequest)
		fmt.Fprint(w, `{"error_type":"INVALID_REQUEST","error_code":"INVALID_FIELD","error_message":"bad field","display_message":null,"request_id":"r"}`)
	}))
	defer server.Close()

	c := newTestClient(server)
	_, _, _, _, err := c.SyncTransactions(t.Context(), "access-token", "")
	if err == nil {
		t.Fatal("expected an error, got nil")
	}
	if calls != 1 {
		t.Fatalf("calls = %d, want 1 — a non-mutation error must not trigger a restart", calls)
	}
}

func TestIsMutationDuringPagination(t *testing.T) {
	mutationErr := plaidSDK.GenericOpenAPIError{}
	mutationErr = plaidSDK.MakeGenericOpenAPIError(nil, "400 Bad Request", plaidSDK.PlaidError{
		ErrorType: "TRANSACTIONS_ERROR",
		ErrorCode: transactionsSyncMutationDuringPagination,
	})
	if !isMutationDuringPagination(fmt.Errorf("plaid: sync transactions: %w", mutationErr)) {
		t.Error("expected true for a wrapped MUTATION_DURING_PAGINATION error")
	}

	otherErr := plaidSDK.MakeGenericOpenAPIError(nil, "400 Bad Request", plaidSDK.PlaidError{
		ErrorType: "INVALID_REQUEST",
		ErrorCode: "INVALID_FIELD",
	})
	if isMutationDuringPagination(fmt.Errorf("plaid: sync transactions: %w", otherErr)) {
		t.Error("expected false for a different error code")
	}

	if isMutationDuringPagination(fmt.Errorf("some unrelated error")) {
		t.Error("expected false for a non-Plaid error")
	}
}
