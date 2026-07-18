package main

import (
	"context"
	"log"
	"os"
	"strconv"
	"time"

	"github.com/BeWellSpent/wellspent-backend/internal/db"
	plaidclient "github.com/BeWellSpent/wellspent-backend/internal/plaid"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	sqlcdb "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"go.uber.org/zap"
)

// plaid-sync fetches incremental transaction changes from Plaid for every active
// plaid_item, then writes new/updated/removed Variable transactions into the
// matching budget period with Plaid-resolved categories.
func main() {
	dbURL := os.Getenv("DATABASE_URL")
	if dbURL == "" {
		log.Fatal("DATABASE_URL is required")
	}
	clientID := os.Getenv("PLAID_CLIENT_ID")
	secret := os.Getenv("PLAID_SECRET")
	plaidEnv := os.Getenv("PLAID_ENV")
	encryptionKey := os.Getenv("ENCRYPTION_KEY")
	if clientID == "" || secret == "" {
		log.Fatal("PLAID_CLIENT_ID and PLAID_SECRET are required")
	}
	if encryptionKey == "" {
		log.Fatal("ENCRYPTION_KEY is required")
	}
	if plaidEnv == "" {
		plaidEnv = "sandbox"
	}
	maxRetries := envIntDefault("PLAID_HTTP_MAX_RETRIES", 3)
	retryDelay := envDurationDefault("PLAID_HTTP_RETRY_DELAY", 5*time.Second)
	redactSensitive := envBoolDefault("PLAID_LOG_REDACT_SENSITIVE", true)

	var logger *zap.Logger
	if os.Getenv("DEBUG") == "true" {
		logger, _ = zap.NewDevelopment()
	} else {
		logger, _ = zap.NewProduction()
	}
	defer logger.Sync() //nolint:errcheck

	ctx := context.Background()
	pool, err := db.NewPool(ctx, dbURL)
	if err != nil {
		log.Fatalf("db: %v", err)
	}
	defer pool.Close()

	queries := sqlcdb.New(pool)
	plaidRepo := repository.NewPlaidRepository(queries)
	budgetRepo := repository.NewBudgetProfileRepository(queries)
	txRepo := repository.NewTransactionRepository(queries)
	feRepo := repository.NewFixedExpenseRepository(queries)
	reviewRepo := repository.NewTransactionReviewRepository(queries)

	pc, err := plaidclient.New(clientID, secret, plaidEnv, plaidclient.Options{
		Logger:          logger,
		RedactSensitive: redactSensitive,
		MaxRetries:      maxRetries,
		RetryDelay:      retryDelay,
	})
	if err != nil {
		log.Fatalf("plaid: init client: %v", err)
	}

	svc := service.NewPlaidService(pc, plaidRepo, budgetRepo, nil, txRepo, feRepo, reviewRepo, encryptionKey)

	items, err := plaidRepo.ListActiveForSync(ctx)
	if err != nil {
		log.Fatalf("list active items: %v", err)
	}

	log.Printf("syncing %d plaid items", len(items))

	for _, item := range items {
		if err := svc.SyncItem(ctx, item); err != nil {
			log.Printf("item %s: sync failed: %v", item.ID, err)
		}
	}
}

func envIntDefault(key string, def int) int {
	v := os.Getenv(key)
	if v == "" {
		return def
	}
	i, err := strconv.Atoi(v)
	if err != nil {
		return def
	}
	return i
}

func envDurationDefault(key string, def time.Duration) time.Duration {
	v := os.Getenv(key)
	if v == "" {
		return def
	}
	d, err := time.ParseDuration(v)
	if err != nil {
		return def
	}
	return d
}

func envBoolDefault(key string, def bool) bool {
	v := os.Getenv(key)
	if v == "" {
		return def
	}
	b, err := strconv.ParseBool(v)
	if err != nil {
		return def
	}
	return b
}
