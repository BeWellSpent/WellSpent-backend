package service

import (
	"context"
	"fmt"
	"log"
	"math"
	"strconv"
	"strings"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"github.com/BeWellSpent/wellspent-backend/internal/crypto"
	plaidclient "github.com/BeWellSpent/wellspent-backend/internal/plaid"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

// SyncItem performs an incremental Plaid transactions sync for a single connected
// item. It imports new transactions, handles modifications and removals, and
// queues or auto-confirms matches against active fixed expenses in the same budget.
// Safe to call from a goroutine; uses a separate context so the caller's request
// context doesn't cancel the background work.
func (s *PlaidService) SyncItem(ctx context.Context, item db.PlaidItem) error {
	categoryIDs, err := s.transactions.ListSystemCategories(ctx)
	if err != nil {
		return err
	}

	cursor := ""
	if item.Cursor != nil {
		cursor = *item.Cursor
	}

	accessToken, err := crypto.Decrypt(item.AccessToken, s.encryptionKey)
	if err != nil {
		return err
	}

	added, modified, removedIDs, nextCursor, err := s.plaid.SyncTransactions(ctx, accessToken, cursor)
	if err != nil {
		_, _ = s.items.UpdateStatus(ctx, db.UpdatePlaidItemStatusParams{
			ID:     item.ID,
			Status: "error",
		})
		return err
	}

	log.Printf("plaid item %s: %d added, %d modified, %d removed", item.ID, len(added), len(modified), len(removedIDs))

	const variableTypeID = 2
	const oneOffFreqID = 1

	pmCache := map[string]*uuid.UUID{}

	fixedExpenses, _ := s.fixedExpenses.List(ctx, item.BudgetProfileID)
	aliasesByFE := make(map[uuid.UUID][]string, len(fixedExpenses))
	for _, fe := range fixedExpenses {
		aliases, _ := s.reviews.ListAliases(ctx, fe.ID)
		aliasesByFE[fe.ID] = aliases
	}

	importedAdded := 0
	autoConfirmed := 0
	queued := 0
	skippedNoPeriod := 0
	skippedDuplicate := 0

	for _, tx := range added {
		date := pgtype.Date{Time: tx.Date, Valid: true}

		period, err := s.budgets.GetPeriodByDate(ctx, item.BudgetProfileID, date)
		if err != nil {
			skippedNoPeriod++
			log.Printf("plaid item %s: skipped %q  %s  $%.2f — no budget period covers this date (plaid_id=%s)",
				item.ID, tx.Name, tx.Date.Format("2006-01-02"), tx.Amount, tx.PlaidID)
			continue
		}

		exists, err := s.transactions.ExistsTransactionByPlaidID(ctx, &tx.PlaidID)
		if err != nil || exists {
			skippedDuplicate++
			log.Printf("plaid item %s: skipped %q  %s  $%.2f — already imported (plaid_id=%s)",
				item.ID, tx.Name, tx.Date.Format("2006-01-02"), tx.Amount, tx.PlaidID)
			continue
		}

		amount := syncAmountToNumeric(tx.Amount)
		plaidID := tx.PlaidID
		periodID := period.ID

		categoryName := syncResolveCategory(tx.Name, tx.PFCPrimary, tx.PFCDetailed)
		var categoryID *int32
		if categoryName != "" {
			if id, ok := categoryIDs[categoryName]; ok {
				categoryID = &id
			}
		}

		var paymentMethodID *uuid.UUID
		if tx.AccountID != "" {
			if pmID, cached := pmCache[tx.AccountID]; cached {
				paymentMethodID = pmID
			} else {
				pm, pmErr := s.transactions.GetPaymentMethodByPlaidAccountID(ctx, tx.AccountID)
				if pmErr == nil {
					id := pm.ID
					paymentMethodID = &id
				}
				pmCache[tx.AccountID] = paymentMethodID
			}
		}

		inserted, err := s.transactions.CreateTransactionFromPlaid(ctx, db.CreateTransactionFromPlaidParams{
			Name:                   &tx.Name,
			Amount:                 amount,
			PlannedAmount:          amount,
			Date:                   date,
			Recurring:              syncBoolPtr(false),
			BudgetPeriodID:         &periodID,
			CategoryID:             categoryID,
			PaymentMethodID:        paymentMethodID,
			TransactionFrequencyID: syncInt32Ptr(oneOffFreqID),
			TransactionTypeID:      syncInt32Ptr(variableTypeID),
			PlaidTransactionID:     &plaidID,
		})
		if err != nil {
			log.Printf("plaid item %s: insert tx %s: %v", item.ID, tx.PlaidID, err)
			continue
		}
		log.Printf("plaid item %s: imported %q  %s  $%.2f  category=%q", item.ID, tx.Name, tx.Date.Format("2006-01-02"), tx.Amount, categoryName)
		importedAdded++

		bestScore, bestFE, bestAliasHit, bestAmountOK := syncScoreBestMatch(tx, categoryID, paymentMethodID, fixedExpenses, aliasesByFE)
		if bestFE == nil {
			continue
		}

		unpaid, upErr := s.fixedExpenses.GetUnpaidTransaction(ctx, db.GetUnpaidTransactionByFixedExpenseParams{
			FixedExpenseID:  bestFE.ID,
			BudgetProfileID: item.BudgetProfileID,
		})
		hasUnpaidTarget := upErr == nil && unpaid.BudgetPeriodID != nil

		switch {
		case bestAliasHit && bestAmountOK && hasUnpaidTarget:
			_, _ = s.transactions.MarkAsPaid(ctx, db.MarkTransactionAsPaidParams{
				ID:             unpaid.ID,
				BudgetPeriodID: *unpaid.BudgetPeriodID,
				Amount:         unpaid.PlannedAmount,
				PaidDate:       unpaid.Date,
			})
			_ = s.transactions.Delete(ctx, db.DeleteTransactionParams{
				ID:             inserted.ID,
				BudgetPeriodID: &periodID,
			})
			importedAdded--
			autoConfirmed++
			log.Printf("plaid item %s: auto-confirmed %q (alias+amount → %q)", item.ID, tx.Name, bestFE.Name)
		case bestScore >= 80 && hasUnpaidTarget:
			if _, rErr := s.reviews.Create(ctx, periodID, inserted.ID, unpaid.ID, bestScore); rErr == nil {
				queued++
				log.Printf("plaid item %s: queued review for %q (score=%.0f, fixed=%q)", item.ID, tx.Name, bestScore, bestFE.Name)
			}
		}
	}

	for _, tx := range modified {
		amount := syncAmountToNumeric(tx.Amount)
		if err := s.transactions.UpdateTransactionFromPlaid(ctx, db.UpdateTransactionFromPlaidParams{
			PlaidTransactionID: &tx.PlaidID,
			Name:               &tx.Name,
			Amount:             amount,
		}); err != nil {
			log.Printf("plaid item %s: update tx %s: %v", item.ID, tx.PlaidID, err)
			continue
		}
		log.Printf("plaid item %s: updated %q  %s  $%.2f", item.ID, tx.Name, tx.Date.Format("2006-01-02"), tx.Amount)
	}

	for _, pid := range removedIDs {
		if err := s.transactions.DeleteTransactionByPlaidID(ctx, &pid); err != nil {
			log.Printf("plaid item %s: delete tx %s: %v", item.ID, pid, err)
			continue
		}
		log.Printf("plaid item %s: removed tx %s", item.ID, pid)
	}

	_, err = s.items.UpdateSync(ctx, db.UpdatePlaidItemSyncParams{
		ID:     item.ID,
		Cursor: &nextCursor,
	})
	if err != nil {
		log.Printf("plaid item %s: update cursor: %v", item.ID, err)
	}

	log.Printf("plaid item %s: done — +%d imported, %d auto-confirmed, %d queued for review, %d modified, %d removed, %d skipped (no period), %d skipped (duplicate)",
		item.ID, importedAdded, autoConfirmed, queued, len(modified), len(removedIDs), skippedNoPeriod, skippedDuplicate)

	// A failure here means the transactions above were imported/updated/removed
	// successfully but the item's cursor wasn't advanced to reflect it — the
	// next run will re-fetch the same batch from Plaid (harmless, since
	// plaid_transaction_id dedup skips re-imports) but should still surface as
	// a failure so it isn't silently retried forever without anyone noticing.
	if err != nil {
		return fmt.Errorf("plaid item %s: persist cursor: %w", item.ID, err)
	}
	return nil
}

const syncAmountTolerance = 3.0

func syncAmountToNumeric(f float64) pgtype.Numeric {
	s := strconv.FormatFloat(f, 'f', 4, 64)
	var n pgtype.Numeric
	_ = n.Scan(s)
	return n
}

func syncBoolPtr(b bool) *bool    { return &b }
func syncInt32Ptr(i int32) *int32 { return &i }

// syncResolveCategory resolves the system category for an imported transaction.
// A name containing "payroll" is treated as income regardless of Plaid's own
// personal-finance-category classification, since payroll deposits should
// never count toward the spending total.
func syncResolveCategory(name, pfcPrimary, pfcDetailed string) string {
	if strings.Contains(strings.ToLower(name), "payroll") {
		return "Income"
	}
	return plaidclient.ResolvePlaidCategory(pfcPrimary, pfcDetailed)
}

func syncAmountWithinTolerance(txAmount float64, fe *db.FixedExpense) bool {
	feAmt, err := fe.PlannedAmount.Float64Value()
	return err == nil && feAmt.Valid && math.Abs(txAmount-feAmt.Float64) <= syncAmountTolerance
}

func syncScoreBestMatch(tx plaidclient.Transaction, categoryID *int32, pmID *uuid.UUID, expenses []db.FixedExpense, aliasesByFE map[uuid.UUID][]string) (float64, *db.FixedExpense, bool, bool) {
	best := 0.0
	var bestFE *db.FixedExpense
	bestAliasHit := false
	bestAmountOK := false
	txNameLower := strings.ToLower(tx.Name)
	for i := range expenses {
		fe := &expenses[i]
		score := 0.0

		amountOK := syncAmountWithinTolerance(tx.Amount, fe)
		if amountOK {
			score += 40
		}

		aliasHit := false
		for _, alias := range aliasesByFE[fe.ID] {
			if strings.EqualFold(alias, tx.Name) {
				aliasHit = true
				break
			}
		}
		feLower := strings.ToLower(fe.Name)
		if aliasHit || syncNameWordsOverlap(txNameLower, feLower) {
			score += 20
		}

		if pmID != nil && fe.PaymentMethodID != nil && *pmID == *fe.PaymentMethodID {
			score += 20
		}
		if categoryID != nil && fe.CategoryID != nil && *categoryID == *fe.CategoryID {
			score += 20
		}

		if score > best {
			best = score
			bestFE = fe
			bestAliasHit = aliasHit
			bestAmountOK = amountOK
		}
	}
	return best, bestFE, bestAliasHit, bestAmountOK
}

func syncNameWordsOverlap(a, b string) bool {
	words := func(s string) map[string]struct{} {
		m := make(map[string]struct{})
		for _, w := range strings.FieldsFunc(s, func(r rune) bool { return !('a' <= r && r <= 'z') }) {
			if len(w) >= 4 {
				m[w] = struct{}{}
			}
		}
		return m
	}
	aw := words(a)
	for w := range words(b) {
		if _, ok := aw[w]; ok {
			return true
		}
	}
	return false
}
