package middleware

import (
	"net"
	"net/http"
	"strings"
	"sync"
	"time"

	"golang.org/x/time/rate"
)

// visitorTTL is how long an idle IP's limiter is kept around before being
// evicted — without this, scanning traffic from many distinct IPs would
// grow the visitor map unbounded.
const visitorTTL = 10 * time.Minute

type visitor struct {
	limiter  *rate.Limiter
	lastSeen time.Time
}

// IPRateLimiter throttles requests per client IP using an independent
// token-bucket limiter per address, so a single source (a scanner, a
// misbehaving client) can't flood the server while unrelated traffic from
// other IPs is unaffected.
type IPRateLimiter struct {
	mu       sync.Mutex
	visitors map[string]*visitor
	rps      rate.Limit
	burst    int
}

func NewIPRateLimiter(rps float64, burst int) *IPRateLimiter {
	return &IPRateLimiter{
		visitors: make(map[string]*visitor),
		rps:      rate.Limit(rps),
		burst:    burst,
	}
}

// StartCleanup runs the idle-visitor eviction loop until ctx is done. Not
// started automatically so tests can call cleanupOnce directly instead of
// depending on wall-clock timing.
func (l *IPRateLimiter) StartCleanup(done <-chan struct{}) {
	ticker := time.NewTicker(time.Minute)
	defer ticker.Stop()
	for {
		select {
		case <-done:
			return
		case <-ticker.C:
			l.cleanupOnce(time.Now())
		}
	}
}

func (l *IPRateLimiter) cleanupOnce(now time.Time) {
	l.mu.Lock()
	defer l.mu.Unlock()
	for ip, v := range l.visitors {
		if now.Sub(v.lastSeen) > visitorTTL {
			delete(l.visitors, ip)
		}
	}
}

func (l *IPRateLimiter) allow(ip string) bool {
	l.mu.Lock()
	v, ok := l.visitors[ip]
	if !ok {
		v = &visitor{limiter: rate.NewLimiter(l.rps, l.burst)}
		l.visitors[ip] = v
	}
	v.lastSeen = time.Now()
	limiter := v.limiter
	l.mu.Unlock()
	return limiter.Allow()
}

// Middleware wraps next with per-IP rate limiting, responding 429 Too Many
// Requests once an address exceeds its bucket.
func (l *IPRateLimiter) Middleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if !l.allow(clientIP(r)) {
			w.Header().Set("Content-Type", "application/json")
			w.WriteHeader(http.StatusTooManyRequests)
			_, _ = w.Write([]byte(`{"code":"resource_exhausted","message":"rate limit exceeded"}`))
			return
		}
		next.ServeHTTP(w, r)
	})
}

// clientIP resolves the originating client address. Cloud Run's front end
// (and any standard reverse proxy/load balancer) sets X-Forwarded-For as
// "client, proxy1, proxy2, ..." — the first entry is the original client;
// trusting it here is safe because Cloud Run's proxy always sets/overwrites
// this header itself, so a direct caller can't spoof their way past it.
func clientIP(r *http.Request) string {
	if fwd := r.Header.Get("X-Forwarded-For"); fwd != "" {
		if idx := strings.Index(fwd, ","); idx != -1 {
			return strings.TrimSpace(fwd[:idx])
		}
		return strings.TrimSpace(fwd)
	}
	host, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		return r.RemoteAddr
	}
	return host
}
