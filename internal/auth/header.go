package auth

import (
	"errors"
	"strings"
)

// ErrMissingBearer is returned when an Authorization header is absent or is not
// a Bearer token. Callers map it to whatever "unauthenticated" means on their
// transport — connect.CodeUnauthenticated, or HTTP 401.
var ErrMissingBearer = errors.New("missing or malformed Authorization header")

// AuthenticateHeader validates a raw Authorization header value and returns the
// user ID it carries.
//
// This exists so there is exactly one implementation of "what counts as a valid
// caller", wrapped twice, rather than two implementations that agree until one
// of them is changed. The Connect interceptor
// (internal/middleware.NewAuthInterceptor) and the REST middleware
// (internal/rest.requireAuth) both call this and differ only in how they report
// failure.
func AuthenticateHeader(jwtSvc *JWTService, authHeader string) (string, error) {
	if jwtSvc == nil {
		return "", ErrMissingBearer
	}
	if !strings.HasPrefix(authHeader, "Bearer ") {
		return "", ErrMissingBearer
	}
	return jwtSvc.ValidateToken(strings.TrimPrefix(authHeader, "Bearer "))
}
