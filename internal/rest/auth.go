package rest

import (
	"net/http"

	"github.com/BeWellSpent/wellspent-backend/internal/auth"
)

// requireAuth enforces the contract's `security: [bearerAuth]` on an operation,
// writing a 401 and reporting false when the caller has no valid token.
//
// Called at the top of the handler rather than installed as middleware, because
// only one of these four endpoints is authenticated: a middleware would have to
// re-derive which route it was running on, duplicating the routing table the
// generated code already owns. This way the check sits exactly where the
// OpenAPI document puts it.
//
// The verification itself is auth.AuthenticateHeader — the same function the
// Connect interceptor calls, so the two transports cannot drift on what counts
// as a valid caller.
func (s *server) requireAuth(w http.ResponseWriter, r *http.Request) (string, bool) {
	userID, err := auth.AuthenticateHeader(s.deps.JWT, r.Header.Get("Authorization"))
	if err != nil {
		writeErrorCode(w, http.StatusUnauthorized, codeUnauthenticated, "a valid bearer token is required")
		return "", false
	}
	return userID, true
}
