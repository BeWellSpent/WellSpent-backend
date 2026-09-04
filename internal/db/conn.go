package db

import (
	"context"
	"fmt"

	"github.com/jackc/pgx/v5/pgxpool"
)

// NewPool opens a pgx pool against databaseURL. appName is sent as the
// Postgres session's application_name — it's what lets a connection be
// identified in pg_stat_activity, since Neon's proxy rewrites client_addr to
// its own internal address and erases the real caller. Every binary that
// dials the DB must pass a distinct one (e.g. "wellspent-server-prod").
func NewPool(ctx context.Context, databaseURL, appName string) (*pgxpool.Pool, error) {
	cfg, err := pgxpool.ParseConfig(databaseURL)
	if err != nil {
		return nil, fmt.Errorf("db: parse config: %w", err)
	}
	cfg.MaxConns = 10
	cfg.MinConns = 0
	cfg.ConnConfig.RuntimeParams["application_name"] = appName

	pool, err := pgxpool.NewWithConfig(ctx, cfg)
	if err != nil {
		return nil, fmt.Errorf("db: connect: %w", err)
	}
	if err := pool.Ping(ctx); err != nil {
		return nil, fmt.Errorf("db: ping: %w", err)
	}
	return pool, nil
}
