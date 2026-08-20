package service

import (
	"context"
	"log"

	"github.com/BeWellSpent/wellspent-backend/internal/repository"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
)

// Marking a fixed transaction paid, in one place.
//
// Three call sites do this: the MarkTransactionAsPaid RPC (the button),
// ConfirmTransactionReview (the To Review tab), and the Plaid sync's
// auto-confirm branch. They used to disagree — only the button rewrote the
// FixedExpense template when the paid amount differed from the plan — so the
// same bill paid three different ways left the next period planned at two
// different figures.
//
// Whether the plan follows the real cost is a budgeting judgement (it tracks
// inflation, which suits some budgets and not others), so it is now
// BudgetProfile.auto_update_planned_amount, decided by the budget owner and
// honoured identically by all three.
//
// Note what does NOT change: the period being paid keeps its own
// planned_amount. Only the template moves, so only future periods are planned
// at the new figure — which is what lets a client show "$90 planned, $67 paid"
// alongside a marker saying the $67 applies from next period on.

// markFixedTransactionPaid marks a transaction paid and, when the budget opts
// in, brings its FixedExpense template up to the amount actually paid.
//
// Every failure is logged with the transaction it concerns. These used to be
// discarded — `_, _ =` on the mark itself in ConfirmTransactionReview, `_ =` on
// the template update — which is how a bill could end up paid for reasons
// nothing recorded, diagnosable only from the database.
func markFixedTransactionPaid(
	ctx context.Context,
	transactions repository.TransactionRepository,
	fixedExpenses repository.FixedExpenseRepository,
	arg db.MarkTransactionAsPaidParams,
	autoUpdatePlannedAmount bool,
	caller string,
) (db.Transaction, error) {
	tx, err := transactions.MarkAsPaid(ctx, arg)
	if err != nil {
		log.Printf("%s: mark transaction %s paid: %v", caller, arg.ID, err)
		return db.Transaction{}, err
	}

	if !autoUpdatePlannedAmount || tx.FixedExpenseID == nil {
		return tx, nil
	}

	// Deliberately unconditional on the amount differing: writing the same
	// value back is harmless, and comparing pgtype.Numeric here would be a
	// second place to get money equality subtly wrong.
	if updateErr := fixedExpenses.UpdatePlannedAmount(ctx, db.UpdateFixedExpensePlannedAmountParams{
		ID:            *tx.FixedExpenseID,
		PlannedAmount: arg.Amount,
	}); updateErr != nil {
		// Not fatal: the payment is recorded and correct. Only the template
		// missed the update, so next period is planned at the old figure — a
		// wrong plan, not a wrong payment, and the user can edit it.
		log.Printf("%s: transaction %s paid, but updating fixed expense %s planned amount failed: %v",
			caller, tx.ID, *tx.FixedExpenseID, updateErr)
	}
	return tx, nil
}

// autoUpdatePlannedAmountFor resolves the budget's setting from a period.
//
// Defaults to true when the profile can't be read, matching both the column
// default and the behaviour every path had before the setting existed: a
// failed lookup must not silently change what marking a bill paid does.
func autoUpdatePlannedAmountFor(
	ctx context.Context,
	profiles repository.BudgetProfileRepository,
	profileID uuid.UUID,
	caller string,
) bool {
	profile, err := profiles.GetByID(ctx, profileID)
	if err != nil {
		log.Printf("%s: read auto_update_planned_amount for profile %s: %v", caller, profileID, err)
		return true
	}
	return profile.AutoUpdatePlannedAmount
}

// paidDateOrDefault picks the date a payment is recorded against, falling back
// to the transaction's own due date — what the automated paths have always
// used, since a bank feed knows when a bill was due but not when the user
// considers it settled.
func paidDateOrDefault(explicit pgtype.Date, fallback pgtype.Date) pgtype.Date {
	if explicit.Valid {
		return explicit
	}
	return fallback
}
