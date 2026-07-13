package main

import (
	"context"
	"log"
	"os"
	"strconv"

	"github.com/jackc/pgx/v5/pgtype"
	"github.com/mauro-afa91/spendsense/internal/crypto"
	"github.com/mauro-afa91/spendsense/internal/db"
	plaidclient "github.com/mauro-afa91/spendsense/internal/plaid"
	"github.com/mauro-afa91/spendsense/internal/repository"
	sqlcdb "github.com/mauro-afa91/spendsense/internal/sqlc"
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

	// Pre-load system category IDs once — used by every transaction import.
	categoryIDs, err := loadCategoryIDs(ctx, queries)
	if err != nil {
		log.Fatalf("load system categories: %v", err)
	}
	log.Printf("loaded %d system categories", len(categoryIDs))

	pc, err := plaidclient.New(clientID, secret, plaidEnv)
	if err != nil {
		log.Fatalf("plaid: init client: %v", err)
	}

	items, err := plaidRepo.ListActiveForSync(ctx)
	if err != nil {
		log.Fatalf("list active items: %v", err)
	}

	log.Printf("syncing %d plaid items", len(items))

	for _, item := range items {
		log.Printf("item %s: starting sync (user %s, budget %s)", item.ID, item.UserID, item.BudgetProfileID)

		cursor := ""
		if item.Cursor != nil {
			cursor = *item.Cursor
		}
		isFirstSync := cursor == ""

		accessToken, err := crypto.Decrypt(item.AccessToken, encryptionKey)
		if err != nil {
			log.Printf("item %s: decrypt access token: %v — skipping", item.ID, err)
			continue
		}

		added, modified, removedIDs, nextCursor, err := pc.SyncTransactions(ctx, accessToken, cursor)
		if err != nil {
			log.Printf("item %s: sync error: %v — marking error", item.ID, err)
			_, _ = plaidRepo.UpdateStatus(ctx, sqlcdb.UpdatePlaidItemStatusParams{
				ID:     item.ID,
				Status: "error",
			})
			continue
		}

		log.Printf("item %s: plaid returned %d added, %d modified, %d removed (first_sync=%v)",
			item.ID, len(added), len(modified), len(removedIDs), isFirstSync)

		// Variable transaction type ID = 2 (seeded in 000001_init_schema.sql).
		// One-off frequency ID = 1.
		const variableTypeID = 2
		const oneOffFreqID = 1

		importedAdded := 0
		skippedNoPeriod := 0
		skippedDuplicate := 0
		for _, tx := range added {
			date := pgtype.Date{Time: tx.Date, Valid: true}

			period, err := budgetRepo.GetPeriodByDate(ctx, item.BudgetProfileID, date)
			if err != nil {
				skippedNoPeriod++
				continue
			}

			exists, err := txRepo.ExistsTransactionByPlaidID(ctx, &tx.PlaidID)
			if err != nil || exists {
				skippedDuplicate++
				continue
			}

			amount := amountToNumeric(tx.Amount)
			plaidID := tx.PlaidID
			periodID := period.ID

			categoryName := plaidclient.ResolvePlaidCategory(tx.PFCPrimary, tx.PFCDetailed)
			var categoryID *int32
			if categoryName != "" {
				if id, ok := categoryIDs[categoryName]; ok {
					categoryID = &id
				}
			}

			_, err = txRepo.CreateTransactionFromPlaid(ctx, sqlcdb.CreateTransactionFromPlaidParams{
				Name:                   &tx.Name,
				Amount:                 amount,
				PlannedAmount:          amount,
				Date:                   date,
				Recurring:              boolPtr(false),
				BudgetPeriodID:         &periodID,
				CategoryID:             categoryID,
				TransactionFrequencyID: int32Ptr(oneOffFreqID),
				TransactionTypeID:      int32Ptr(variableTypeID),
				PlaidTransactionID:     &plaidID,
			})
			if err != nil {
				log.Printf("item %s: insert tx %s: %v", item.ID, tx.PlaidID, err)
				continue
			}
			importedAdded++
		}

		for _, tx := range modified {
			amount := amountToNumeric(tx.Amount)
			if err := txRepo.UpdateTransactionFromPlaid(ctx, sqlcdb.UpdateTransactionFromPlaidParams{
				PlaidTransactionID: &tx.PlaidID,
				Name:               &tx.Name,
				Amount:             amount,
			}); err != nil {
				log.Printf("item %s: update tx %s: %v", item.ID, tx.PlaidID, err)
			}
		}

		for _, pid := range removedIDs {
			if err := txRepo.DeleteTransactionByPlaidID(ctx, &pid); err != nil {
				log.Printf("item %s: delete tx %s: %v", item.ID, pid, err)
			}
		}

		_, err = plaidRepo.UpdateSync(ctx, sqlcdb.UpdatePlaidItemSyncParams{
			ID:     item.ID,
			Cursor: &nextCursor,
		})
		if err != nil {
			log.Printf("item %s: update cursor: %v", item.ID, err)
		}

		log.Printf("item %s: done — +%d imported, %d modified, %d removed, %d skipped (no period), %d skipped (duplicate)",
			item.ID, importedAdded, len(modified), len(removedIDs), skippedNoPeriod, skippedDuplicate)
	}
}

// loadCategoryIDs queries all system categories and returns a name→id map.
func loadCategoryIDs(ctx context.Context, q *sqlcdb.Queries) (map[string]int32, error) {
	rows, err := q.ListSystemCategories(ctx)
	if err != nil {
		return nil, err
	}
	m := make(map[string]int32, len(rows))
	for _, r := range rows {
		m[r.Name] = r.ID
	}
	return m, nil
}

func amountToNumeric(f float64) pgtype.Numeric {
	s := strconv.FormatFloat(f, 'f', 4, 64)
	var n pgtype.Numeric
	_ = n.Scan(s)
	return n
}

func boolPtr(b bool) *bool    { return &b }
func int32Ptr(i int32) *int32 { return &i }
