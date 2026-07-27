package main

import (
	"context"
	"log"
	"os"
	"time"

	"github.com/jackc/pgx/v5/pgtype"
	"github.com/BeWellSpent/wellspent-backend/internal/db"
	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	"github.com/BeWellSpent/wellspent-backend/internal/service"
	sqlcdb "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

// cycle-budgets is a daily job that finds every BudgetProfile whose latest period
// has ended (end_date < today) and creates the next period, pre-filling recurring
// income entries and carrying forward fixed+recurring transactions.
func main() {
	dbURL := os.Getenv("DATABASE_URL")
	if dbURL == "" {
		log.Fatal("DATABASE_URL is required")
	}

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

	svc := service.NewBudgetProfileService(profileRepo, txRepo, fixedExpenseRepo, userRepo)

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
