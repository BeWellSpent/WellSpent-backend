// Package rest serves the handful of endpoints that are global — the identical
// response for every caller — and rarely-changing, over plain cacheable HTTP.
//
// Everything else in this API is ConnectRPC and stays that way. The boundary is
// deliberate and narrow; WellSpent-proto's openapi/README.md holds the rule and
// the candidates that were considered and rejected. The short version: HTTP
// caching is the only thing this transport buys, so an endpoint that cannot be
// cached does not belong here.
//
// These handlers own no business logic. Each one calls the same
// internal/service method the retired Connect handler called, and differs only
// in how the result is written — which is the point: the migration changed the
// transport and nothing else.
package rest

import (
	"net/http"

	restgen "github.com/BeWellSpent/wellspent-backend/gen/rest"
	"github.com/BeWellSpent/wellspent-backend/internal/auth"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	"go.uber.org/zap"
)

// Deps is everything the REST endpoints need. Services are shared with the
// Connect handlers rather than duplicated — same instances, same wiring in
// cmd/server/main.go.
type Deps struct {
	Users         *service.UserService
	StatusBanners *service.StatusBannerService
	Changelog     *service.ChangelogService
	JWT           *auth.JWTService
	Logger        *zap.Logger
	ServerVersion string
}

type server struct {
	deps Deps
}

var _ restgen.ServerInterface = (*server)(nil)

// Register mounts the REST routes on the caller's mux.
//
// Taking the existing mux rather than returning a new handler is what makes
// this free: cmd/server/main.go already wraps that one mux in h2c, the IP rate
// limiter and CORS, so these routes inherit all three with no second server, no
// second middleware chain, and nothing to keep in sync.
func Register(mux *http.ServeMux, deps Deps) {
	if deps.Logger == nil {
		panic("rest.Register: Logger is required")
	}
	restgen.HandlerWithOptions(&server{deps: deps}, restgen.StdHTTPServerOptions{
		BaseRouter: mux,
		// The generated default writes plain text via http.Error. A client
		// generated from the same contract expects the Error schema on every
		// failure, including a malformed query parameter.
		ErrorHandlerFunc: func(w http.ResponseWriter, r *http.Request, err error) {
			writeErrorCode(w, http.StatusBadRequest, codeInvalidArgument, err.Error())
		},
	})
}
