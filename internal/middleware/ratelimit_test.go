package middleware

import (
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func newTestHandler(t *testing.T, rps float64, burst int) (*IPRateLimiter, http.Handler) {
	t.Helper()
	l := NewIPRateLimiter(rps, burst)
	h := l.Middleware(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
	}))
	return l, h
}

func doRequest(h http.Handler, remoteAddr string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(http.MethodGet, "/", nil)
	req.RemoteAddr = remoteAddr
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req)
	return rec
}

func TestIPRateLimiter_AllowsWithinBurst(t *testing.T) {
	_, h := newTestHandler(t, 1, 3)
	for i := 0; i < 3; i++ {
		rec := doRequest(h, "1.2.3.4:5555")
		require.Equal(t, http.StatusOK, rec.Code, "request %d should be allowed within burst", i+1)
	}
}

func TestIPRateLimiter_RejectsOverBurst(t *testing.T) {
	_, h := newTestHandler(t, 1, 3)
	for i := 0; i < 3; i++ {
		doRequest(h, "1.2.3.4:5555")
	}
	rec := doRequest(h, "1.2.3.4:5555")
	assert.Equal(t, http.StatusTooManyRequests, rec.Code)
}

func TestIPRateLimiter_TracksIPsIndependently(t *testing.T) {
	_, h := newTestHandler(t, 1, 1)
	rec1 := doRequest(h, "1.1.1.1:1")
	rec2 := doRequest(h, "2.2.2.2:2")
	assert.Equal(t, http.StatusOK, rec1.Code)
	assert.Equal(t, http.StatusOK, rec2.Code, "a different IP should have its own bucket")
}

func TestIPRateLimiter_UsesFirstXForwardedForEntry(t *testing.T) {
	l, h := newTestHandler(t, 1, 1)
	req := httptest.NewRequest(http.MethodGet, "/", nil)
	req.RemoteAddr = "10.0.0.1:1234" // the Cloud Run proxy's own address
	req.Header.Set("X-Forwarded-For", "9.9.9.9, 10.0.0.1")
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req)
	require.Equal(t, http.StatusOK, rec.Code)

	l.mu.Lock()
	_, tracked := l.visitors["9.9.9.9"]
	l.mu.Unlock()
	assert.True(t, tracked, "should key the limiter by the original client IP, not the proxy address")
}

func TestIPRateLimiter_CleanupEvictsIdleVisitors(t *testing.T) {
	l, _ := newTestHandler(t, 1, 1)
	l.allow("3.3.3.3")

	l.mu.Lock()
	_, tracked := l.visitors["3.3.3.3"]
	l.mu.Unlock()
	require.True(t, tracked)

	l.cleanupOnce(time.Now().Add(visitorTTL + time.Minute))

	l.mu.Lock()
	_, stillTracked := l.visitors["3.3.3.3"]
	l.mu.Unlock()
	assert.False(t, stillTracked, "idle visitor should be evicted once past the TTL")
}

func TestClientIP_FallsBackToRemoteAddr(t *testing.T) {
	req := httptest.NewRequest(http.MethodGet, "/", nil)
	req.RemoteAddr = "5.6.7.8:9999"
	assert.Equal(t, "5.6.7.8", clientIP(req))
}
