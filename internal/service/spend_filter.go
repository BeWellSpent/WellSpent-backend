package service

import db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"

// What counts as spend, in one place.
//
// Two rules decide whether a transaction is real spending activity, and they
// are shared by everything that totals money server-side: the Expense Summary
// (which computes the balance the user is shown) and the period carryover
// (which turns that balance into next period's transactions). They must agree —
// a carried figure that disagrees with the figure on screen is worse than no
// carryover at all.
//
// Both clients also re-implement these rules for their own row rendering. That
// duplication is the drift risk docs/features/expense-summary.md records
// (issue #35); keeping the server's copy singular is the part within reach.

// isNonSpendTransaction reports whether a transaction should be left out of
// spending totals entirely: explicitly excluded by the user, or filed under the
// Income system category (a payroll deposit is not a purchase).
func isNonSpendTransaction(tx db.Transaction, incomeCategoryID int32, hasIncomeCategory bool) bool {
	if tx.IsExcluded {
		return true
	}
	return hasIncomeCategory && tx.CategoryID != nil && *tx.CategoryID == incomeCategoryID
}

// isUnpaidFixed reports whether a transaction is a Fixed obligation that hasn't
// been marked paid. Those are planned, not spent — they count toward a
// category's plan but never toward its actual.
func isUnpaidFixed(tx db.Transaction) bool {
	return tx.TransactionTypeID != nil && *tx.TransactionTypeID == fixedTransactionTypeID && !tx.IsPaid
}
