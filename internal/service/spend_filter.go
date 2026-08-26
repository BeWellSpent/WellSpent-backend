package service

import (
	"github.com/BeWellSpent/wellspent-backend/internal/category"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
)

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

// nonSpendSystemCategories lists the system categories whose transactions are
// never spending, whatever their sign or type.
//
//   - Income: a payroll deposit is not a purchase.
//
//   - Payment: a credit card payment settles a balance that was already counted
//     when it was spent. Critically it is imported TWICE — positive on the
//     account paying and negative on the card being paid — so counting both
//     made every linked card cancel out its own purchases. A card carrying
//     $1,477.86 of real spend reported −$29.01, while the checking account that
//     paid it absorbed the difference and looked like it had done the spending.
//     The grand total happened to survive because the two legs sum to zero, but
//     only for as long as both accounts stay linked; the per-payment-method
//     figures were never right. internal/plaid/category.go routes Plaid's
//     LOAN_PAYMENTS_CREDIT_CARD_PAYMENT here for exactly this reason.
//
// Transfer is deliberately NOT in this list. It mixes money moved between the
// user's own accounts with Zelle payments to real people, and both are genuine
// balance movements that already carry the right sign: money out is spend,
// money in reduces it. Only the card-payment case is double-counted, so only
// the card-payment case is removed.
var nonSpendSystemCategories = []category.Key{category.Income, category.Payment}

// nonSpendCategoryIDs resolves nonSpendSystemCategories against the seeded
// category rows. A key may legitimately be missing (not yet seeded in a fresh
// environment), in which case nothing is filtered on it.
func nonSpendCategoryIDs(systemCategories map[category.Key]int32) map[int32]bool {
	ids := make(map[int32]bool, len(nonSpendSystemCategories))
	for _, key := range nonSpendSystemCategories {
		if id, ok := systemCategories[key]; ok {
			ids[id] = true
		}
	}
	return ids
}

// isNonSpendTransaction reports whether a transaction should be left out of
// spending totals entirely: explicitly excluded by the user, or filed under one
// of the categories above.
func isNonSpendTransaction(tx db.Transaction, nonSpend map[int32]bool) bool {
	if tx.IsExcluded {
		return true
	}
	return tx.CategoryID != nil && nonSpend[*tx.CategoryID]
}

// isUnpaidFixed reports whether a transaction is a Fixed obligation that hasn't
// been marked paid. Those are planned, not spent — they count toward a
// category's plan but never toward its actual.
func isUnpaidFixed(tx db.Transaction) bool {
	return tx.TransactionTypeID != nil && *tx.TransactionTypeID == fixedTransactionTypeID && !tx.IsPaid
}

// isFixed reports whether tx is a Fixed-type transaction. Companion to
// isUnpaidFixed, which additionally requires it to be unpaid.
func isFixed(tx db.Transaction) bool {
	return tx.TransactionTypeID != nil && *tx.TransactionTypeID == fixedTransactionTypeID
}
