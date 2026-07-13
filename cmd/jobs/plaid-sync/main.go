package main

import (
	"context"
	"log"
	"math"
	"os"
	"strconv"
	"strings"

	"github.com/google/uuid"
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
	feRepo := repository.NewFixedExpenseRepository(queries)
	reviewRepo := repository.NewTransactionReviewRepository(queries)

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

		// Cache plaid_account_id → payment_method_id for this item's sync run.
		pmCache := map[string]*uuid.UUID{}

		// Pre-load active fixed expenses once per item for match scoring.
		fixedExpenses, _ := feRepo.List(ctx, item.BudgetProfileID)

		importedAdded := 0
		autoConfirmed := 0
		queued := 0
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
			log.Printf("item %s: importing tx %q amount=%.2f pfc=%s/%s -> category=%q", item.ID, tx.Name, tx.Amount, tx.PFCPrimary, tx.PFCDetailed, categoryName)
			var categoryID *int32
			if categoryName != "" {
				if id, ok := categoryIDs[categoryName]; ok {
					categoryID = &id
				}
			}

			// Resolve payment method from cached plaid_account_id lookup.
			var paymentMethodID *uuid.UUID
			if tx.AccountID != "" {
				if pmID, cached := pmCache[tx.AccountID]; cached {
					paymentMethodID = pmID
				} else {
					pm, pmErr := txRepo.GetPaymentMethodByPlaidAccountID(ctx, tx.AccountID)
					if pmErr == nil {
						id := pm.ID
						paymentMethodID = &id
					}
					pmCache[tx.AccountID] = paymentMethodID
				}
			}

			inserted, err := txRepo.CreateTransactionFromPlaid(ctx, sqlcdb.CreateTransactionFromPlaidParams{
				Name:                   &tx.Name,
				Amount:                 amount,
				PlannedAmount:          amount,
				Date:                   date,
				Recurring:              boolPtr(false),
				BudgetPeriodID:         &periodID,
				CategoryID:             categoryID,
				PaymentMethodID:        paymentMethodID,
				TransactionFrequencyID: int32Ptr(oneOffFreqID),
				TransactionTypeID:      int32Ptr(variableTypeID),
				PlaidTransactionID:     &plaidID,
			})
			if err != nil {
				log.Printf("item %s: insert tx %s: %v", item.ID, tx.PlaidID, err)
				continue
			}
			importedAdded++

			// Alias match → auto-confirm: mark the fixed tx paid, delete the import.
			if aliasMatch, aliasErr := reviewRepo.GetFixedExpenseByAlias(ctx, tx.Name, item.BudgetProfileID); aliasErr == nil {
				unpaid, upErr := feRepo.GetUnpaidTransaction(ctx, sqlcdb.GetUnpaidTransactionByFixedExpenseParams{
					FixedExpenseID:  aliasMatch.ID,
					BudgetProfileID: item.BudgetProfileID,
				})
				if upErr == nil && unpaid.BudgetPeriodID != nil {
					_, _ = txRepo.MarkAsPaid(ctx, sqlcdb.MarkTransactionAsPaidParams{
						ID:             unpaid.ID,
						BudgetPeriodID: *unpaid.BudgetPeriodID,
						Amount:         unpaid.PlannedAmount,
						PaidDate:       unpaid.Date,
					})
				}
				_ = txRepo.Delete(ctx, sqlcdb.DeleteTransactionParams{
					ID:             inserted.ID,
					BudgetPeriodID: &periodID,
				})
				importedAdded--
				autoConfirmed++
				log.Printf("item %s: auto-confirmed %q (alias → %q)", item.ID, tx.Name, aliasMatch.Name)
				continue
			}

			// Score against fixed expense templates — queue for review if ≥ 80.
			bestScore, bestFE := scoreBestMatch(tx, categoryID, paymentMethodID, fixedExpenses)
			if bestScore >= 80 && bestFE != nil {
				if _, rErr := reviewRepo.Create(ctx, periodID, inserted.ID, bestFE.ID, bestScore); rErr == nil {
					queued++
					log.Printf("item %s: queued review for %q (score=%.0f, fixed=%q)", item.ID, tx.Name, bestScore, bestFE.Name)
				}
			}
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

		log.Printf("item %s: done — +%d imported, %d auto-confirmed, %d queued for review, %d modified, %d removed, %d skipped (no period), %d skipped (duplicate)",
			item.ID, importedAdded, autoConfirmed, queued, len(modified), len(removedIDs), skippedNoPeriod, skippedDuplicate)
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

// scoreBestMatch returns the highest match score and the corresponding fixed
// expense. Weights: amount 40% ($3 tolerance), name 20%, payment method 20%,
// category 20%.
func scoreBestMatch(tx plaidclient.Transaction, categoryID *int32, pmID *uuid.UUID, expenses []sqlcdb.FixedExpense) (float64, *sqlcdb.FixedExpense) {
	best := 0.0
	var bestFE *sqlcdb.FixedExpense
	txNameLower := strings.ToLower(tx.Name)
	for i := range expenses {
		fe := &expenses[i]
		score := 0.0

		// Amount: 40pts within $3
		feAmt, err := fe.PlannedAmount.Float64Value()
		if err == nil && feAmt.Valid && math.Abs(tx.Amount-feAmt.Float64) <= 3.0 {
			score += 40
		}

		// Name: 20pts — case-insensitive substring
		feLower := strings.ToLower(fe.Name)
		if strings.Contains(txNameLower, feLower) || strings.Contains(feLower, txNameLower) {
			score += 20
		}

		// Payment method: 20pts
		if pmID != nil && fe.PaymentMethodID != nil && *pmID == *fe.PaymentMethodID {
			score += 20
		}

		// Category: 20pts
		if categoryID != nil && fe.CategoryID != nil && *categoryID == *fe.CategoryID {
			score += 20
		}

		if score > best {
			best = score
			bestFE = fe
		}
	}
	return best, bestFE
}
