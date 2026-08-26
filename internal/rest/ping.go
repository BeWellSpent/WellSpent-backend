package rest

import (
	"net/http"

	restgen "github.com/BeWellSpent/wellspent-backend/gen/rest"
)

// GetPing reports that the REST transport is serving.
//
// Deliberately trivial and deliberately kept: it is the only endpoint here that
// touches no service and no database, so when a REST call fails it answers
// "is the transport broken, or is this endpoint broken?" without a second
// guess. Uncached — caching a liveness probe defeats it.
func (s *server) GetPing(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, r, s.deps.Logger, cacheNone, "", restgen.PingResponse{
		Status:        "ok",
		ServerVersion: s.deps.ServerVersion,
	})
}
