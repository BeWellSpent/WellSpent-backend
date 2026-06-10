package middleware

import (
	"context"
	"strings"

	"connectrpc.com/connect"
	"github.com/mauro-afa91/spendsense/internal/auth"
)

type contextKey string

const UserIDKey contextKey = "user_id"

// NewAuthInterceptor validates JWT tokens for all procedures except those in bypass.
func NewAuthInterceptor(jwtSvc *auth.JWTService, bypass map[string]bool) connect.UnaryInterceptorFunc {
	return func(next connect.UnaryFunc) connect.UnaryFunc {
		return func(ctx context.Context, req connect.AnyRequest) (connect.AnyResponse, error) {
			if bypass[req.Spec().Procedure] {
				return next(ctx, req)
			}
			authHeader := req.Header().Get("Authorization")
			if !strings.HasPrefix(authHeader, "Bearer ") {
				return nil, connect.NewError(connect.CodeUnauthenticated, nil)
			}
			token := strings.TrimPrefix(authHeader, "Bearer ")
			userID, err := jwtSvc.ValidateToken(token)
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
