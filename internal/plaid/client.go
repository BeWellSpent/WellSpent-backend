package plaid

import (
	"context"
	"errors"
	"fmt"
	"log"
	"net/http"
	"time"

	plaidSDK "github.com/plaid/plaid-go/v20/plaid"
	"go.uber.org/zap"
)

// Account is a Plaid-linked bank account normalised for SpendSense.
type Account struct {
	PlaidAccountID string
	Name           string
	Mask           string // last 4 digits of account number; may be empty
	Type           string // depository, credit, investment, loan, other
	Subtype        string // checking, savings, credit card, etc.
}

// Transaction is a Plaid transaction normalised for import into SpendSense.
type Transaction struct {
	PlaidID     string
	AccountID   string  // Plaid account_id — links to Account.PlaidAccountID
	Name        string
	Amount      float64 // positive = debit (spending), negative = credit (received)
	Date        time.Time
	PFCPrimary  string // personal_finance_category.primary (e.g. "FOOD_AND_DRINK")
	PFCDetailed string // personal_finance_category.detailed (e.g. "FOOD_AND_DRINK_GROCERIES")
}

// Client is a thin, mockable wrapper around the Plaid API.
type Client interface {
	// CreateLinkToken creates a Plaid Link token for the given user.
	CreateLinkToken(ctx context.Context, userID string) (linkToken, expiration string, err error)
	// ExchangePublicToken exchanges a Link public token for a permanent access token.
	ExchangePublicToken(ctx context.Context, publicToken string) (accessToken, itemID string, err error)
	// GetInstitutionName returns the display name for a Plaid institution ID.
	GetInstitutionName(ctx context.Context, institutionID string) (string, error)
	// RemoveItem notifies Plaid that the item should be deauthorised.
	RemoveItem(ctx context.Context, accessToken string) error
	// GetAccounts returns all accounts linked to the access token and the
	// Plaid institution ID (empty string if unavailable).
	GetAccounts(ctx context.Context, accessToken string) (accounts []Account, institutionID string, err error)
	// SyncTransactions fetches incremental transaction changes since cursor.
	// Pass an empty cursor for the initial sync.
	SyncTransactions(ctx context.Context, accessToken, cursor string) (added, modified []Transaction, removedIDs []string, nextCursor string, err error)
}

type client struct {
	api *plaidSDK.APIClient
}

// Options configures the HTTP-level behavior of a live Plaid client — request/
// response logging and retry-on-failure. MaxRetries/RetryDelay fall back to
// NewLoggingRetryTransport's defaults (3 retries, 5s apart) when zero.
// RedactSensitive has no implicit default here — callers should resolve it
// from config (PLAID_LOG_REDACT_SENSITIVE, default "true" — see
// internal/config) before constructing Options, since these are bank-account
// credentials and "safe by default" shouldn't depend on remembering to set a
// field.
type Options struct {
	Logger          *zap.Logger
	RedactSensitive bool
	MaxRetries      int
	RetryDelay      time.Duration
}

// New builds a live Plaid API client.
// env must be "sandbox" or "production".
func New(clientID, secret, env string, opts Options) (Client, error) {
	cfg := plaidSDK.NewConfiguration()
	cfg.AddDefaultHeader("PLAID-CLIENT-ID", clientID)
	cfg.AddDefaultHeader("PLAID-SECRET", secret)

	switch env {
	case "production":
		cfg.UseEnvironment(plaidSDK.Production)
	default:
		cfg.UseEnvironment(plaidSDK.Sandbox)
	}

	cfg.HTTPClient = &http.Client{
		Transport: NewLoggingRetryTransport(nil, TransportConfig{
			Logger:          opts.Logger,
			RedactSensitive: opts.RedactSensitive,
			MaxRetries:      opts.MaxRetries,
			RetryDelay:      opts.RetryDelay,
		}),
	}

	return &client{api: plaidSDK.NewAPIClient(cfg)}, nil
}

func (c *client) CreateLinkToken(ctx context.Context, userID string) (string, string, error) {
	user := plaidSDK.LinkTokenCreateRequestUser{ClientUserId: userID}
	req := plaidSDK.NewLinkTokenCreateRequest("WellSpent", "en", []plaidSDK.CountryCode{plaidSDK.COUNTRYCODE_US}, user)
	req.SetProducts([]plaidSDK.Products{plaidSDK.PRODUCTS_TRANSACTIONS})

	resp, _, err := c.api.PlaidApi.LinkTokenCreate(ctx).LinkTokenCreateRequest(*req).Execute()
	if err != nil {
		return "", "", fmt.Errorf("plaid: create link token: %w", err)
	}
	exp := resp.GetExpiration().Format(time.RFC3339)
	return resp.GetLinkToken(), exp, nil
}

func (c *client) ExchangePublicToken(ctx context.Context, publicToken string) (string, string, error) {
	req := plaidSDK.NewItemPublicTokenExchangeRequest(publicToken)
	resp, _, err := c.api.PlaidApi.ItemPublicTokenExchange(ctx).ItemPublicTokenExchangeRequest(*req).Execute()
	if err != nil {
		return "", "", fmt.Errorf("plaid: exchange public token: %w", err)
	}
	return resp.GetAccessToken(), resp.GetItemId(), nil
}

func (c *client) GetInstitutionName(ctx context.Context, institutionID string) (string, error) {
	req := plaidSDK.NewInstitutionsGetByIdRequest(institutionID, []plaidSDK.CountryCode{plaidSDK.COUNTRYCODE_US})
	resp, _, err := c.api.PlaidApi.InstitutionsGetById(ctx).InstitutionsGetByIdRequest(*req).Execute()
	if err != nil {
		return "", fmt.Errorf("plaid: get institution: %w", err)
	}
	inst := resp.GetInstitution()
	return inst.Name, nil
}

func (c *client) RemoveItem(ctx context.Context, accessToken string) error {
	req := plaidSDK.NewItemRemoveRequest(accessToken)
	_, _, err := c.api.PlaidApi.ItemRemove(ctx).ItemRemoveRequest(*req).Execute()
	if err != nil {
		return fmt.Errorf("plaid: remove item: %w", err)
	}
	return nil
}

func (c *client) GetAccounts(ctx context.Context, accessToken string) ([]Account, string, error) {
	req := plaidSDK.NewAccountsGetRequest(accessToken)
	resp, _, err := c.api.PlaidApi.AccountsGet(ctx).AccountsGetRequest(*req).Execute()
	if err != nil {
		return nil, "", fmt.Errorf("plaid: get accounts: %w", err)
	}

	institutionID := ""
	if id := resp.Item.GetInstitutionId(); id != "" {
		institutionID = id
	}

	accounts := make([]Account, 0, len(resp.GetAccounts()))
	for _, a := range resp.GetAccounts() {
		mask := ""
		if m, ok := a.GetMaskOk(); ok && m != nil {
			mask = *m
		}
		subtype := ""
		if st, ok := a.GetSubtypeOk(); ok && st != nil {
			subtype = string(*st)
		}
		accounts = append(accounts, Account{
			PlaidAccountID: a.GetAccountId(),
			Name:           a.GetName(),
			Mask:           mask,
			Type:           string(a.GetType()),
			Subtype:        subtype,
		})
	}
	return accounts, institutionID, nil
}

// maxSyncPaginationRestarts bounds how many times SyncTransactions will
// restart a full paginated fetch after a TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION
// error, Plaid's documented recovery for that error.
const maxSyncPaginationRestarts = 3

// transactionsSyncMutationDuringPagination is returned by Plaid when the
// underlying transaction data changes while a multi-page /transactions/sync
// fetch is in progress. Plaid's guidance: discard the in-progress pages and
// restart the whole fetch from the cursor you started with.
const transactionsSyncMutationDuringPagination = "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION"

func (c *client) SyncTransactions(ctx context.Context, accessToken, cursor string) ([]Transaction, []Transaction, []string, string, error) {
	var err error
	for attempt := 0; attempt <= maxSyncPaginationRestarts; attempt++ {
		if attempt > 0 {
			// Plaid data is still mutating — wait before retrying so the
			// underlying changes have time to settle. Without this pause the
			// restarts hammer the same in-flux data and fail identically.
			delay := time.Duration(attempt) * 2 * time.Second
			log.Printf("plaid: TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION (attempt %d/%d) — waiting %s before restart", attempt, maxSyncPaginationRestarts, delay)
			select {
			case <-time.After(delay):
			case <-ctx.Done():
				return nil, nil, nil, "", ctx.Err()
			}
		}
		var added, modified []Transaction
		var removedIDs []string
		var nextCursor string
		added, modified, removedIDs, nextCursor, err = c.syncTransactionsAllPages(ctx, accessToken, cursor)
		if err == nil {
			return added, modified, removedIDs, nextCursor, nil
		}
		if !isMutationDuringPagination(err) {
			return nil, nil, nil, "", err
		}
	}
	log.Printf("plaid: TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION: all %d restart attempts exhausted", maxSyncPaginationRestarts+1)
	return nil, nil, nil, "", err
}

// syncTransactionsAllPages drains every page of a single logical sync
// starting at cursor, following has_more until Plaid reports no more pages.
// Persisting an intermediate (has_more=true) cursor is unsafe — per Plaid's
// docs, only the cursor returned once has_more is false is guaranteed
// stable/durable, and stopping early both silently drops any changes beyond
// the first page and risks a MUTATION_DURING_PAGINATION error on the next
// scheduled sync.
func (c *client) syncTransactionsAllPages(ctx context.Context, accessToken, cursor string) ([]Transaction, []Transaction, []string, string, error) {
	var added, modified []Transaction
	var removedIDs []string

	for {
		req := plaidSDK.NewTransactionsSyncRequest(accessToken)
		if cursor != "" {
			req.SetCursor(cursor)
		}
		// Explicitly request personal_finance_category — without this flag some
		// Plaid integrations return nil PFC, causing the credit-card-payment filter
		// to silently pass through those transactions.
		opts := plaidSDK.NewTransactionsSyncRequestOptions()
		opts.SetIncludePersonalFinanceCategory(true)
		req.SetOptions(*opts)

		resp, _, err := c.api.PlaidApi.TransactionsSync(ctx).TransactionsSyncRequest(*req).Execute()
		if err != nil {
			return nil, nil, nil, "", fmt.Errorf("plaid: sync transactions: %w", err)
		}

		added = append(added, toTransactions(resp.GetAdded())...)
		modified = append(modified, toTransactions(resp.GetModified())...)
		for _, r := range resp.GetRemoved() {
			removedIDs = append(removedIDs, r.GetTransactionId())
		}

		cursor = resp.GetNextCursor()
		if !resp.GetHasMore() {
			return added, modified, removedIDs, cursor, nil
		}
	}
}

func isMutationDuringPagination(err error) bool {
	var apiErr plaidSDK.GenericOpenAPIError
	if !errors.As(err, &apiErr) {
		return false
	}
	plaidErr, ok := apiErr.Model().(plaidSDK.PlaidError)
	return ok && plaidErr.GetErrorCode() == transactionsSyncMutationDuringPagination
}

// isCreditCardPayment returns true for transactions that represent a credit
// card bill payment seen from either account's perspective:
//   - LOAN_PAYMENTS_CREDIT_CARD_PAYMENT  — outflow from the checking account
//   - LOAN_DISBURSEMENTS_OTHER_DISBURSEMENT — inflow on the credit card account
//     ("Payment Thank You" type entries)
func isCreditCardPayment(t plaidSDK.Transaction) bool {
	if pfc, ok := t.GetPersonalFinanceCategoryOk(); ok && pfc != nil {
		d := pfc.GetDetailed()
		return d == "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT" ||
			d == "LOAN_DISBURSEMENTS_OTHER_DISBURSEMENT"
	}
	return false
}

func toTransactions(ts []plaidSDK.Transaction) []Transaction {
	out := make([]Transaction, 0, len(ts))
	for _, t := range ts {
		if isCreditCardPayment(t) {
			continue
		}

		// Prefer authorized_date (when the user made the purchase) over date
		// (when the bank settled it). The posted date can be 1–3 days later,
		// which would mis-route boundary transactions to the wrong period.
		dateStr := t.GetDate()
		if ad, ok := t.GetAuthorizedDateOk(); ok && ad != nil && *ad != "" {
			dateStr = *ad
		}
		d, err := time.Parse("2006-01-02", dateStr)
		if err != nil {
			continue
		}
		// Prefer the Plaid-enriched merchant name when available — it's a
		// cleaner, more human-readable version of the raw bank string.
		name := t.GetName()
		if mn := t.GetMerchantName(); mn != "" {
			name = mn
		}

		primary, detailed := "", ""
		if pfc, ok := t.GetPersonalFinanceCategoryOk(); ok && pfc != nil {
			primary = pfc.GetPrimary()
			detailed = pfc.GetDetailed()
		}

		out = append(out, Transaction{
			PlaidID:     t.GetTransactionId(),
			AccountID:   t.GetAccountId(),
			Name:        name,
			Amount:      t.GetAmount(),
			Date:        d,
			PFCPrimary:  primary,
			PFCDetailed: detailed,
		})
	}
	return out
}
