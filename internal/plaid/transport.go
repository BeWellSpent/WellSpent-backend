package plaid

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"
	"time"

	"go.uber.org/zap"
)

// maxLoggedBodyBytes caps how much of a request/response body gets logged —
// TransactionsSync responses can be large, and Cloud Run Logging bills by
// volume.
const maxLoggedBodyBytes = 4096

// sensitiveJSONFields are redacted from logged bodies when RedactSensitive
// is true. These are credentials/bearer tokens, not general transaction PII —
// the whole point of this wrapper is to see request/response detail (like the
// 400 that prompted it), so only the fields that could leak account access
// are candidates for redaction.
var sensitiveJSONFields = map[string]bool{
	"client_id":    true,
	"secret":       true,
	"access_token": true,
	"public_token": true,
	"link_token":   true,
}

// sensitiveHeaders are always redacted, regardless of RedactSensitive — the
// API credentials themselves should never be recoverable from logs under any
// configuration.
var sensitiveHeaders = map[string]bool{
	"Plaid-Client-Id": true,
	"Plaid-Secret":    true,
}

// TransportConfig configures NewLoggingRetryTransport.
type TransportConfig struct {
	Logger *zap.Logger

	// RedactSensitive scrubs credential/token fields (client_id, secret,
	// access_token, public_token, link_token) from logged bodies. Defaults
	// to true (safe) when constructed via New's Options — set false only for
	// short-lived deep debugging. Does not affect the PLAID-CLIENT-ID /
	// PLAID-SECRET headers, which are always redacted.
	RedactSensitive bool

	// MaxRetries is how many additional attempts are made after the first
	// failure. Defaults to 3 if <= 0.
	MaxRetries int

	// RetryDelay is the pause between attempts. Defaults to 5s if <= 0.
	RetryDelay time.Duration
}

// NewLoggingRetryTransport wraps base (or http.DefaultTransport if nil) with
// request/response logging and retry-on-failure.
//
// Only retries conditions a second attempt could plausibly fix: network-level
// errors, 429 Too Many Requests, and 5xx server errors. 4xx responses (like
// the 400 Bad Request that prompted this) are the request's fault — retrying
// an identical request just fails identically three more times and burns
// API quota, so those are logged and returned immediately without retry.
func NewLoggingRetryTransport(base http.RoundTripper, cfg TransportConfig) http.RoundTripper {
	if base == nil {
		base = http.DefaultTransport
	}
	if cfg.MaxRetries <= 0 {
		cfg.MaxRetries = 3
	}
	if cfg.RetryDelay <= 0 {
		cfg.RetryDelay = 5 * time.Second
	}
	if cfg.Logger == nil {
		cfg.Logger = zap.NewNop()
	}
	return &loggingRetryTransport{base: base, cfg: cfg}
}

type loggingRetryTransport struct {
	base http.RoundTripper
	cfg  TransportConfig
}

func (t *loggingRetryTransport) RoundTrip(req *http.Request) (*http.Response, error) {
	var reqBody []byte
	if req.Body != nil {
		var err error
		reqBody, err = io.ReadAll(req.Body)
		_ = req.Body.Close()
		if err != nil {
			return nil, err
		}
	}

	var lastResp *http.Response
	var lastErr error

	for attempt := 0; attempt <= t.cfg.MaxRetries; attempt++ {
		if reqBody != nil {
			req.Body = io.NopCloser(bytes.NewReader(reqBody))
			req.ContentLength = int64(len(reqBody))
		}

		t.logRequest(req, reqBody, attempt)

		resp, err := t.base.RoundTrip(req)
		lastResp, lastErr = resp, err

		if err != nil {
			t.cfg.Logger.Warn("plaid.http.error",
				zap.String("url", req.URL.String()),
				zap.Int("attempt", attempt+1),
				zap.Error(err),
			)
			if attempt == t.cfg.MaxRetries {
				return nil, err
			}
		} else {
			t.logResponse(req, resp, attempt)

			if !shouldRetry(resp.StatusCode) || attempt == t.cfg.MaxRetries {
				return resp, nil
			}
			_ = resp.Body.Close()
		}

		select {
		case <-req.Context().Done():
			return lastResp, req.Context().Err()
		case <-time.After(t.cfg.RetryDelay):
		}
	}

	return lastResp, lastErr
}

func shouldRetry(status int) bool {
	return status == http.StatusTooManyRequests || status >= 500
}

func (t *loggingRetryTransport) logRequest(req *http.Request, body []byte, attempt int) {
	fields := []zap.Field{
		zap.String("method", req.Method),
		zap.String("url", req.URL.String()),
		zap.Int("attempt", attempt+1),
		zap.Any("headers", redactHeaders(req.Header)),
	}
	if len(body) > 0 {
		fields = append(fields, zap.String("body", formatBody(body, t.cfg.RedactSensitive)))
	}
	t.cfg.Logger.Info("plaid.http.request", fields...)
}

// logResponse reads and re-attaches resp.Body (a RoundTripper must return a
// body the caller can still read) after logging it.
func (t *loggingRetryTransport) logResponse(req *http.Request, resp *http.Response, attempt int) {
	var body []byte
	if resp.Body != nil {
		body, _ = io.ReadAll(resp.Body)
		_ = resp.Body.Close()
		resp.Body = io.NopCloser(bytes.NewReader(body))
	}

	fields := []zap.Field{
		zap.String("url", req.URL.String()),
		zap.Int("status", resp.StatusCode),
		zap.Int("attempt", attempt+1),
	}
	if len(body) > 0 {
		fields = append(fields, zap.String("body", formatBody(body, t.cfg.RedactSensitive)))
	}
	t.cfg.Logger.Info("plaid.http.response", fields...)
}

func redactHeaders(h http.Header) map[string]string {
	out := make(map[string]string, len(h))
	for k, v := range h {
		if len(v) == 0 {
			continue
		}
		if sensitiveHeaders[http.CanonicalHeaderKey(k)] {
			out[k] = "REDACTED"
			continue
		}
		out[k] = v[0]
	}
	return out
}

// formatBody returns body as a string, redacting sensitiveJSONFields (at any
// nesting depth) when redact is true, and truncating to maxLoggedBodyBytes.
// Falls back to logging the raw (still-truncated) bytes if the body isn't
// valid JSON.
func formatBody(body []byte, redact bool) string {
	out := body
	if redact {
		var parsed any
		if err := json.Unmarshal(body, &parsed); err == nil {
			redactValue(parsed)
			if reencoded, err := json.Marshal(parsed); err == nil {
				out = reencoded
			}
		}
	}
	if len(out) > maxLoggedBodyBytes {
		return string(out[:maxLoggedBodyBytes]) + "...(truncated)"
	}
	return string(out)
}

// redactValue walks a decoded JSON value in place, replacing the value of
// any object key in sensitiveJSONFields with "REDACTED".
func redactValue(v any) {
	switch val := v.(type) {
	case map[string]any:
		for k, child := range val {
			if sensitiveJSONFields[k] {
				val[k] = "REDACTED"
				continue
			}
			redactValue(child)
		}
	case []any:
		for _, child := range val {
			redactValue(child)
		}
	}
}
