package middleware

import (
	"context"

	"connectrpc.com/connect"
	"github.com/BeWellSpent/wellspent-backend/internal/auth"
)

type contextKey string

const UserIDKey contextKey = "user_id"

// NewAuthInterceptor validates JWT tokens for all procedures except those in bypass.
//
// The token check itself lives in auth.AuthenticateHeader, shared with the REST
// transport (internal/rest) so both agree on what a valid caller is by
// construction rather than by review.
func NewAuthInterceptor(jwtSvc *auth.JWTService, bypass map[string]bool) connect.UnaryInterceptorFunc {
	return func(next connect.UnaryFunc) connect.UnaryFunc {
		return func(ctx context.Context, req connect.AnyRequest) (connect.AnyResponse, error) {
			if bypass[req.Spec().Procedure] {
				return next(ctx, req)
			}
			userID, err := auth.AuthenticateHeader(jwtSvc, req.Header().Get("Authorization"))
			if err != nil {
				return nil, connect.NewError(connect.CodeUnauthenticated, err)
			}
			ctx = context.WithValue(ctx, UserIDKey, userID)
			return next(ctx, req)
		}
	}
}

func UserIDFromContext(ctx context.Context) (string, bool) {
	v, ok := ctx.Value(UserIDKey).(string)
	return v, ok
}
