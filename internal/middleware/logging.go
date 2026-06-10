package middleware

import (
	"context"
	"time"

	"connectrpc.com/connect"
	"go.uber.org/zap"
)

func NewLoggingInterceptor(logger *zap.Logger) connect.UnaryInterceptorFunc {
	return func(next connect.UnaryFunc) connect.UnaryFunc {
		return func(ctx context.Context, req connect.AnyRequest) (connect.AnyResponse, error) {
			start := time.Now()
			resp, err := next(ctx, req)
			fields := []zap.Field{
				zap.String("procedure", req.Spec().Procedure),
				zap.Duration("duration", time.Since(start)),
			}
			if err != nil {
				fields = append(fields, zap.Error(err))
				logger.Warn("rpc error", fields...)
			} else {
				logger.Info("rpc ok", fields...)
			}
			return resp, err
		}
	}
}
