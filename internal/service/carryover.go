package service

import (
	"math/big"
	"sort"

	"github.com/BeWellSpent/wellspent-backend/internal/category"
	db "github.com/BeWellSpent/wellspent-backend/internal/sqlc"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
)

// Carryover: turning a closed period's ending balance into transactions in the
// next one (issue #53).
//
// The whole feature is one number — income minus actual spend for the period
// that just closed:
//
//   - Leftover  → a single Savings transaction, no payment method.
//   - Shortfall → Debt transactions split across every payment method the money
//     was spent on, in proportion to each method's share of the period's spend.
//
// A payment method never carries an *independent* balance. Deriving "how much
// is still owed on this credit card" would mean netting the card's own rows
// against payments recorded on it, and on a manual budget nobody records card
// payments — every card would look permanently maxed out. So a payment method
// is only ever used to attribute a shortfall that already exists, which is
// what makes the carried debt show where it came from.
//
// Everything here is pure: no I/O, no clock, no repository. The remainder and
// the per-method spend are computed once by the caller from the same filtered
// transaction set the Expense Summary uses, so the number carried forward is
// always the number the user was shown.

// carryoverCategorySavings and carryoverCategoryDebt are the system categories
// a carried row is filed under. Both are resolved through ListSystemCategories
// by the caller; Debt was seeded by migration 000052.
const (
	carryoverCategorySavings = category.Savings
	carryoverCategoryDebt    = category.Debt
)

// nanosPerCent is the quantum every carried amount is rounded to. Amounts are
// apportioned in whole cents rather than raw nanos so a split never produces
// something like $234.782608696, which would display as $234.78 while being
// silently worth more.
const nanosPerCent = 10_000_000

// carryoverRow is one transaction to create in the new period.
type carryoverRow struct {
	amountNanos int64
	categoryKey category.Key
	// paymentMethodID is nil for the leftover row (a surplus belongs to no
	// particular method) and for the share of a shortfall attributable to
	// spend that had no payment method recorded against it.
	paymentMethodID *uuid.UUID
}

// carryoverBucket is one claimant on a shortfall: a payment method and what was
// spent on it. Held as an ordered slice rather than a map because Go randomises
// map iteration, and two runs of the cycling job over identical input must
// produce identical amounts down to the cent.
type carryoverBucket struct {
	methodID  *uuid.UUID // nil = spend with no payment method recorded
	spendNano int64
}

// computeCarryover turns a period's ending balance into the rows to create in
// the next period.
//
// remainderNanos is income minus actual spend: positive means money left over,
// negative means the budget ended short. spendByMethod is what was spent on
// each payment method over the same filtered transaction set, and
// unattributedSpendNanos is the spend that had no payment method at all (which
// takes a proportional share of a shortfall like any other bucket, and becomes
// a row with no payment method).
//
// Returns nil when the period ended exactly even.
func computeCarryover(
	remainderNanos int64,
	spendByMethod map[uuid.UUID]int64,
	unattributedSpendNanos int64,
) []carryoverRow {
	if remainderNanos == 0 {
		return nil
	}

	// Leftover: one row, no method, filed as Savings. Nothing is owed, so
	// there is nothing to attribute to a payment method.
	if remainderNanos > 0 {
		return []carryoverRow{{
			amountNanos: remainderNanos,
			categoryKey: carryoverCategorySavings,
		}}
	}

	shortfall := -remainderNanos
	buckets, totalSpend := carryoverBuckets(spendByMethod, unattributedSpendNanos)

	// A shortfall with no positive spend behind it can't be attributed. It
	// takes negative income to reach here, which the app has no way to record,
	// but one unattributed row is the honest fallback — silently dropping the
	// balance is the exact bug this feature exists to fix.
	if totalSpend <= 0 {
		return []carryoverRow{{
			amountNanos: shortfall,
			categoryKey: carryoverCategoryDebt,
		}}
	}

	shares := apportion(shortfall, buckets, totalSpend)

	rows := make([]carryoverRow, 0, len(buckets))
	for i, share := range shares {
		if share == 0 {
			continue
		}
		rows = append(rows, carryoverRow{
			amountNanos:     share,
			categoryKey:     carryoverCategoryDebt,
			paymentMethodID: buckets[i].methodID,
		})
	}
	return rows
}

// carryoverBuckets collects the payment methods that can absorb a share of a
// shortfall, highest spend first.
//
// Only positive spend qualifies. A method whose net is zero or negative (more
// received on it than spent) contributed nothing to the overspend, so giving it
// a share would pin debt on an account that was never the cause.
func carryoverBuckets(spendByMethod map[uuid.UUID]int64, unattributedSpendNanos int64) ([]carryoverBucket, int64) {
	buckets := make([]carryoverBucket, 0, len(spendByMethod)+1)
	total := int64(0)
	for id, spend := range spendByMethod {
		if spend <= 0 {
			continue
		}
		methodID := id
		buckets = append(buckets, carryoverBucket{methodID: &methodID, spendNano: spend})
		total += spend
	}
	if unattributedSpendNanos > 0 {
		buckets = append(buckets, carryoverBucket{spendNano: unattributedSpendNanos})
		total += unattributedSpendNanos
	}

	sort.Slice(buckets, func(i, j int) bool {
		if buckets[i].spendNano != buckets[j].spendNano {
			return buckets[i].spendNano > buckets[j].spendNano
		}
		return bucketSortKey(buckets[i]) < bucketSortKey(buckets[j])
	})
	return buckets, total
}

// bucketSortKey breaks ties between equal-spend buckets. The unattributed
// bucket sorts last, since a named payment method is the more useful place for
// a rounding cent to land.
func bucketSortKey(b carryoverBucket) string {
	if b.methodID == nil {
		return "￿"
	}
	return b.methodID.String()
}

// apportion splits amount across buckets in proportion to their spend, in whole
// cents, returning one nanos figure per bucket in the order given.
//
// Uses the largest-remainder method: floor every share, then hand out the
// leftover cents to whichever buckets lost the most to that flooring. This is
// what makes the shares sum to the amount *exactly* — a user comparing the
// carried rows against the balance they were shown must not find them a cent
// apart — while still landing on the intuitive per-bucket figure (a $300
// shortfall over $1,800 debit and $500 credit splits $234.78 / $65.22).
//
// Any sub-cent remainder of the amount itself goes to the largest bucket, so
// nothing is lost when the balance isn't a whole number of cents (the amount
// column is NUMERIC(15,4), so it need not be).
func apportion(amount int64, buckets []carryoverBucket, totalSpend int64) []int64 {
	totalCents := amount / nanosPerCent
	subCent := amount % nanosPerCent

	shares := make([]int64, len(buckets))
	type remainder struct {
		idx       int
		rem       int64
		spendNano int64
	}
	remainders := make([]remainder, len(buckets))
	assigned := int64(0)

	for i, b := range buckets {
		// numerator = totalCents * spend, in big.Int so the product of two
		// nanos-scaled figures cannot overflow int64, and so no float64 ever
		// enters money arithmetic (see internal/handler/convert.go's
		// moneyFromNumeric, where a float64 intermediate produced amounts off
		// by one nano and broke an exact-equality check).
		num := new(big.Int).Mul(big.NewInt(totalCents), big.NewInt(b.spendNano))
		quo, rem := new(big.Int).QuoRem(num, big.NewInt(totalSpend), new(big.Int))
		shares[i] = quo.Int64()
		remainders[i] = remainder{idx: i, rem: rem.Int64(), spendNano: b.spendNano}
		assigned += shares[i]
	}

	// Largest lost fraction first; ties go to the bigger spender, then to the
	// earlier bucket (which carryoverBuckets already ordered deterministically).
	sort.Slice(remainders, func(i, j int) bool {
		if remainders[i].rem != remainders[j].rem {
			return remainders[i].rem > remainders[j].rem
		}
		if remainders[i].spendNano != remainders[j].spendNano {
			return remainders[i].spendNano > remainders[j].spendNano
		}
		return remainders[i].idx < remainders[j].idx
	})
	for k := int64(0); k < totalCents-assigned; k++ {
		shares[remainders[int(k)%len(remainders)].idx]++
	}

	for i := range shares {
		shares[i] *= nanosPerCent
	}
	if subCent > 0 && len(shares) > 0 {
		shares[0] += subCent // buckets[0] is the highest-spend bucket
	}
	return shares
}

// numericFromNanos converts a nanos-scaled int64 to the pgtype.Numeric the
// transaction table stores, keeping the value exact.
func numericFromNanos(totalNanos int64) pgtype.Numeric {
	return pgtype.Numeric{Int: big.NewInt(totalNanos), Exp: -9, Valid: true}
}

// carryoverInputs reduces a closing period's raw transactions and income
// entries to the three numbers computeCarryover needs.
//
// The filter is the shared one in spend_filter.go, not a private copy, so the
// balance carried forward is by construction the balance the Expense Summary
// showed the user: same exclusions, same treatment of unpaid Fixed rows.
func carryoverInputs(
	txs []db.Transaction,
	incomeEntries []db.IncomeEntry,
	nonSpend map[int32]bool,
) (remainderNanos int64, spendByMethod map[uuid.UUID]int64, unattributedSpendNanos int64) {
	spendByMethod = map[uuid.UUID]int64{}

	var totalSpend int64
	for _, tx := range txs {
		if isNonSpendTransaction(tx, nonSpend) || isUnpaidFixed(tx) {
			continue
		}
		amt := numericToNanos(tx.Amount)
		totalSpend += amt
		if tx.PaymentMethodID == nil {
			unattributedSpendNanos += amt
			continue
		}
		spendByMethod[*tx.PaymentMethodID] += amt
	}

	var income int64
	for _, e := range incomeEntries {
		income += numericToNanos(e.Amount)
	}

	return income - totalSpend, spendByMethod, unattributedSpendNanos
}
