package rest

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"net/http"
	"strings"

	"go.uber.org/zap"
)

// Cache-Control policies. These are the whole reason these endpoints left
// Connect, so they are named constants rather than string literals scattered
// across the controllers.
const (
	// cacheCountries — the country list is three rows that change roughly
	// never. A day of freshness with a week of stale-while-revalidate means a
	// returning visitor essentially never waits on this request.
	cacheCountries = "public, max-age=86400, stale-while-revalidate=604800"

	// cacheStatusBanner — short, because the entire point of a banner is that
	// it appears during an incident. 30 seconds is the most delay worth
	// trading for taking this request off the origin on every page load.
	cacheStatusBanner = "public, max-age=30, stale-while-revalidate=60"

	// cacheChangelog — private only. The response is identical for every
	// authenticated caller, but a shared cache would need `Vary: Authorization`
	// to be correct, which fragments it per token and saves nothing.
	cacheChangelog = "private, max-age=3600"

	// cacheNone — the probe. Caching a liveness check defeats it.
	cacheNone = "no-store"
)

// writeJSON writes a 200 with the given body, cache policy, and ETag, honouring
// a matching If-None-Match with a 304.
//
// The ETag is computed by the caller from the *data*, not from the serialized
// bytes, so it stays stable across an unrelated change to the JSON encoder or a
// field reordering. Pass an empty etag to skip conditional handling entirely.
func writeJSON(w http.ResponseWriter, r *http.Request, log *zap.Logger, cacheControl, etag string, body any) {
	w.Header().Set("Cache-Control", cacheControl)

	if etag != "" {
		w.Header().Set("ETag", etag)
		if matchesETag(r.Header.Get("If-None-Match"), etag) {
			// 304 carries no body, and per RFC 9110 must carry the same
			// caching headers it would have sent with a 200 — otherwise a
			// revalidation silently downgrades the client's cache policy.
			w.WriteHeader(http.StatusNotModified)
			return
		}
	}

	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	if err := json.NewEncoder(w).Encode(body); err != nil {
		// The status line is already out, so this cannot become an error
		// response. Log it and move on.
		log.Error("rest: encode response", zap.String("path", r.URL.Path), zap.Error(err))
	}
}

// writeError renders a service-layer error as JSON with the mapped status.
func writeError(w http.ResponseWriter, r *http.Request, log *zap.Logger, err error) {
	status, code := statusForError(err)
	if status >= http.StatusInternalServerError {
		log.Error("rest: request failed", zap.String("path", r.URL.Path), zap.Error(err))
	}
	writeErrorCode(w, status, code, err.Error())
}

// writeErrorCode renders an explicit status/code pair, for failures that never
// pass through the service layer (an absent bearer token, say).
func writeErrorCode(w http.ResponseWriter, status int, code errorCode, message string) {
	w.Header().Set("Content-Type", "application/json")
	// An error is never the cached representation of a resource. Without this
	// a shared cache could hold a 500 for the whole max-age of the endpoint
	// that produced it.
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(errorBody{Code: string(code), Message: message})
}

// matchesETag reports whether an If-None-Match header selects the given tag.
//
// Handles the comma-separated list form and the `*` wildcard, and compares
// weakly: a W/ prefix on either side is ignored, which is correct here because
// these responses are semantically rather than byte-for-byte equivalent.
func matchesETag(ifNoneMatch, etag string) bool {
	if ifNoneMatch == "" || etag == "" {
		return false
	}
	if strings.TrimSpace(ifNoneMatch) == "*" {
		return true
	}
	want := strings.TrimPrefix(etag, "W/")
	for _, candidate := range strings.Split(ifNoneMatch, ",") {
		if strings.TrimPrefix(strings.TrimSpace(candidate), "W/") == want {
			return true
		}
	}
	return false
}

// makeETag builds a quoted entity tag from the parts that identify a version of
// a resource.
//
// Hashed rather than concatenated so a tag never leaks an internal identifier
// or grows unbounded with the payload, and truncated to 16 bytes because an
// ETag only needs to be collision-resistant against *the same resource's* other
// versions, not globally.
func makeETag(parts ...string) string {
	sum := sha256.Sum256([]byte(strings.Join(parts, "\x00")))
	return fmt.Sprintf("%q", hex.EncodeToString(sum[:16]))
}
