package main

import (
	"context"
	"fmt"
	"log"
	"os"
	"time"

	"github.com/jackc/pgx/v5/pgtype"
	"github.com/joho/godotenv"
	"github.com/BeWellSpent/wellspent-backend/internal/config"
	"github.com/BeWellSpent/wellspent-backend/internal/db"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	sqlcdb "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"go.uber.org/zap"
)

// cycle-budgets is a daily job that finds every BudgetProfile whose latest period
// has ended (end_date < today) and creates the next period, pre-filling recurring
// income entries and carrying forward fixed+recurring transactions.
func main() {
	env := os.Getenv("ENV")
	if env == "" {
		env = "dev"
	}
	// Overload (not Load) so the file always wins over any stale env var in the shell.
	if err := godotenv.Overload(fmt.Sprintf(".env.%s", env)); err != nil {
		log.Printf("[WARN] could not load .env.%s: %v", env, err)
	}

	dbURL := os.Getenv("DATABASE_URL")
	if dbURL == "" {
		log.Fatalf("[FATAL] DATABASE_URL is not set (checked .env.%s)", env)
	}

	// Lite notification config, same pattern as cmd/jobs/plaid-sync/main.go —
	// this job creates budget periods, which fires the period_created alert
	// (in-app/email/push) via NotificationService.HandlePeriodCreated. All
	// fields are optional; each channel just no-ops with a log warning if
	// its config is missing (APNSAuthKey empty, ResendAPIKey empty, etc.).
	notifCfg := &config.Config{
		ResendAPIKey:    os.Getenv("RESEND_API_KEY"),
		ResendFromEmail: envStringDefault("RESEND_FROM_EMAIL", "WellSpent <noreply@wellspent.app>"),
		FrontendURL:     envStringDefault("FRONTEND_URL", "http://localhost:3000"),
		APNSKeyID:       os.Getenv("APNS_KEY_ID"),
		APNSTeamID:      os.Getenv("APNS_TEAM_ID"),
		APNSAuthKey:     config.NormalizePEMKey(os.Getenv("APNS_AUTH_KEY")),
		APNSBundleID:    envStringDefault("APNS_BUNDLE_ID", "com.bewellspent.WellSpent"),
		APNSEnvironment: envStringDefault("APNS_ENVIRONMENT", "sandbox"),
	}

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
	profileRepo      := repository.NewBudgetProfileRepository(queries)
	txRepo           := repository.NewTransactionRepository(queries)
	fixedExpenseRepo := repository.NewFixedExpenseRepository(queries)
	userRepo         := repository.NewUserRepository(queries)
	allocationRepo   := repository.NewExpenseAllocationRepository(queries)
	notifRepo        := repository.NewNotificationRepository(queries)

	notifSvc := service.NewNotificationService(notifRepo, txRepo, profileRepo, allocationRepo, userRepo, notifCfg, logger)
	svc := service.NewBudgetProfileService(profileRepo, txRepo, fixedExpenseRepo, userRepo).WithNotifications(notifSvc)

	today := time.Now().UTC()
	yesterday := today.AddDate(0, 0, -1)
	cutoff := pgtype.Date{Time: yesterday, Valid: true}

	profileIDs, err := profileRepo.ListProfileIDsWithExpiredPeriod(ctx, cutoff)
	if err != nil {
		log.Fatalf("[FATAL] list expired profiles: %v", err)
	}

	log.Printf("[START] found %d profile(s) with an expired period to cycle", len(profileIDs))

	succeeded, failed := 0, 0
	for _, id := range profileIDs {
		profile, err := profileRepo.GetByID(ctx, id)
		if err != nil {
			log.Printf("[ERROR] %s: failed to load profile: %v", id, err)
			failed++
			continue
		}

		log.Printf("[PICK]  %s — %q (%s cycle): starting cycle", id, profile.Name, profile.Cycle)

		period, err := svc.CreateBudgetPeriod(ctx, id, profile.UserID)
		if err != nil {
			log.Printf("[ERROR] %s — %q: failed to create next period: %v", id, profile.Name, err)
			failed++
			continue
		}

		log.Printf("[OK]    %s — %q: new period %s → %s",
			id, profile.Name,
			period.StartDate.Time.Format("2006-01-02"),
			period.EndDate.Time.Format("2006-01-02"),
		)
		succeeded++
	}

	log.Printf("[DONE]  %d cycled, %d failed", succeeded, failed)
	if failed > 0 {
		log.Printf("[WARN]  %d profile(s) failed to cycle — check errors above", failed)
	}
}

func envStringDefault(key, def string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return def
}
