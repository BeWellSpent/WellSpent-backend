package plaid

import (
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
	"time"

	"go.uber.org/zap"
)

type roundTripFunc func(*http.Request) (*http.Response, error)

func (f roundTripFunc) RoundTrip(req *http.Request) (*http.Response, error) { return f(req) }

func newTestRequest(t *testing.T, body string) *http.Request {
	t.Helper()
	req, err := http.NewRequest(http.MethodPost, "https://sandbox.plaid.com/transactions/sync", strings.NewReader(body))
	if err != nil {
		t.Fatalf("build request: %v", err)
	}
	req.Header.Set("PLAID-CLIENT-ID", "client-123")
	req.Header.Set("PLAID-SECRET", "shh-secret")
	req.Header.Set("Content-Type", "application/json")
	return req
}

func readBody(t *testing.T, resp *http.Response) string {
	t.Helper()
	b, err := io.ReadAll(resp.Body)
	if err != nil {
		t.Fatalf("read body: %v", err)
	}
	return string(b)
}

func TestRoundTrip_SuccessNoRetry(t *testing.T) {
	calls := 0
	base := roundTripFunc(func(req *http.Request) (*http.Response, error) {
		calls++
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader(`{"ok":true}`))}, nil
	})
	rt := NewLoggingRetryTransport(base, TransportConfig{Logger: zap.NewNop(), RetryDelay: time.Millisecond})

	resp, err := rt.RoundTrip(newTestRequest(t, `{"cursor":""}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if resp.StatusCode != 200 {
		t.Fatalf("status = %d, want 200", resp.StatusCode)
	}
	if calls != 1 {
		t.Fatalf("calls = %d, want 1 (no retry on success)", calls)
	}
	if got := readBody(t, resp); got != `{"ok":true}` {
		t.Fatalf("body = %q, want response body preserved for caller", got)
	}
}

func TestRoundTrip_RetriesOn5xxThenSucceeds(t *testing.T) {
	calls := 0
	base := roundTripFunc(func(req *http.Request) (*http.Response, error) {
		calls++
		if calls < 3 {
			return &http.Response{StatusCode: 500, Body: io.NopCloser(strings.NewReader(`{"error":"internal"}`))}, nil
		}
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader(`{"ok":true}`))}, nil
	})
	rt := NewLoggingRetryTransport(base, TransportConfig{Logger: zap.NewNop(), MaxRetries: 3, RetryDelay: time.Millisecond})

	resp, err := rt.RoundTrip(newTestRequest(t, `{}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if resp.StatusCode != 200 {
		t.Fatalf("status = %d, want 200 after retries", resp.StatusCode)
	}
	if calls != 3 {
		t.Fatalf("calls = %d, want 3 (2 failures + 1 success)", calls)
	}
}

func TestRoundTrip_NoRetryOn400(t *testing.T) {
	calls := 0
	base := roundTripFunc(func(req *http.Request) (*http.Response, error) {
		calls++
		return &http.Response{StatusCode: 400, Body: io.NopCloser(strings.NewReader(`{"error_code":"INVALID_REQUEST"}`))}, nil
	})
	rt := NewLoggingRetryTransport(base, TransportConfig{Logger: zap.NewNop(), MaxRetries: 3, RetryDelay: time.Millisecond})

	resp, err := rt.RoundTrip(newTestRequest(t, `{}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if resp.StatusCode != 400 {
		t.Fatalf("status = %d, want 400", resp.StatusCode)
	}
	if calls != 1 {
		t.Fatalf("calls = %d, want 1 — a 400 is the caller's fault and won't succeed on retry", calls)
	}
}

func TestRoundTrip_RetriesOnNetworkError(t *testing.T) {
	calls := 0
	base := roundTripFunc(func(req *http.Request) (*http.Response, error) {
		calls++
		if calls < 2 {
			return nil, errors.New("connection reset")
		}
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader(`{"ok":true}`))}, nil
	})
	rt := NewLoggingRetryTransport(base, TransportConfig{Logger: zap.NewNop(), MaxRetries: 3, RetryDelay: time.Millisecond})

	resp, err := rt.RoundTrip(newTestRequest(t, `{}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if resp.StatusCode != 200 {
		t.Fatalf("status = %d, want 200", resp.StatusCode)
	}
	if calls != 2 {
		t.Fatalf("calls = %d, want 2", calls)
	}
}

func TestRoundTrip_ExhaustsMaxRetries(t *testing.T) {
	calls := 0
	base := roundTripFunc(func(req *http.Request) (*http.Response, error) {
		calls++
		return &http.Response{StatusCode: 503, Body: io.NopCloser(strings.NewReader(`{"error":"unavailable"}`))}, nil
	})
	rt := NewLoggingRetryTransport(base, TransportConfig{Logger: zap.NewNop(), MaxRetries: 2, RetryDelay: time.Millisecond})

	resp, err := rt.RoundTrip(newTestRequest(t, `{}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if resp.StatusCode != 503 {
		t.Fatalf("status = %d, want 503 (last attempt's result)", resp.StatusCode)
	}
	if calls != 3 {
		t.Fatalf("calls = %d, want 3 (1 initial + 2 retries)", calls)
	}
}

func TestRoundTrip_DefaultsApplied(t *testing.T) {
	calls := 0
	base := roundTripFunc(func(req *http.Request) (*http.Response, error) {
		calls++
		return &http.Response{StatusCode: 500, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
	})
	// Zero-value TransportConfig should fall back to 3 retries — override
	// only RetryDelay so the test doesn't take 15s.
	rt := NewLoggingRetryTransport(base, TransportConfig{RetryDelay: time.Millisecond})

	_, err := rt.RoundTrip(newTestRequest(t, `{}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if calls != 4 {
		t.Fatalf("calls = %d, want 4 (default MaxRetries=3 -> 1 initial + 3 retries)", calls)
	}
}

func TestRedactHeaders(t *testing.T) {
	h := http.Header{}
	h.Set("PLAID-CLIENT-ID", "client-123")
	h.Set("PLAID-SECRET", "shh-secret")
	h.Set("Content-Type", "application/json")

	out := redactHeaders(h)

	if out["Plaid-Client-Id"] != "REDACTED" {
		t.Errorf("Plaid-Client-Id = %q, want REDACTED", out["Plaid-Client-Id"])
	}
	if out["Plaid-Secret"] != "REDACTED" {
		t.Errorf("Plaid-Secret = %q, want REDACTED", out["Plaid-Secret"])
	}
	if out["Content-Type"] != "application/json" {
		t.Errorf("Content-Type = %q, want preserved", out["Content-Type"])
	}
}

func TestFormatBody_RedactsSensitiveFields(t *testing.T) {
	body := []byte(`{"client_id":"abc","secret":"xyz","access_token":"tok","cursor":"c1","nested":{"public_token":"pt"}}`)

	got := formatBody(body, true)

	var parsed map[string]any
	if err := json.Unmarshal([]byte(got), &parsed); err != nil {
		t.Fatalf("output not valid JSON: %v", err)
	}
	for _, key := range []string{"client_id", "secret", "access_token"} {
		if parsed[key] != "REDACTED" {
			t.Errorf("%s = %v, want REDACTED", key, parsed[key])
		}
	}
	if parsed["cursor"] != "c1" {
		t.Errorf("cursor = %v, want preserved (not a sensitive field)", parsed["cursor"])
	}
	nested, ok := parsed["nested"].(map[string]any)
	if !ok || nested["public_token"] != "REDACTED" {
		t.Errorf("nested.public_token not redacted: %v", parsed["nested"])
	}
}

func TestFormatBody_NoRedactWhenDisabled(t *testing.T) {
	body := []byte(`{"secret":"xyz","cursor":"c1"}`)

	got := formatBody(body, false)

	if !strings.Contains(got, `"xyz"`) {
		t.Errorf("body = %q, want secret preserved when RedactSensitive=false", got)
	}
}

func TestFormatBody_Truncates(t *testing.T) {
	body := bytes.Repeat([]byte("a"), maxLoggedBodyBytes+500)

	got := formatBody(body, false)

	if !strings.HasSuffix(got, "...(truncated)") {
		t.Errorf("expected truncation suffix, got suffix %q", got[len(got)-20:])
	}
	if len(got) > maxLoggedBodyBytes+len("...(truncated)") {
		t.Errorf("truncated body still too long: %d bytes", len(got))
	}
}

func TestFormatBody_NonJSONDoesNotPanic(t *testing.T) {
	got := formatBody([]byte("not json"), true)
	if got != "not json" {
		t.Errorf("body = %q, want raw passthrough for non-JSON", got)
	}
}
