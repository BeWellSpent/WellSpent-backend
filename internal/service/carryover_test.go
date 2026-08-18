package service

import (
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// dollars converts a plain dollar figure to the nanos scale computeCarryover
// works in, so the tests below read as the money they are about.
func dollars(d float64) int64 {
	return int64(d*100+0.5) * nanosPerCent
}

// sumRows totals a carryover result. Every shortfall case asserts this equals
// the shortfall exactly: rows that don't add up to the balance the user was
// shown are the failure mode the apportionment exists to prevent.
func sumRows(rows []carryoverRow) int64 {
	var total int64
	for _, r := range rows {
		total += r.amountNanos
	}
	return total
}

func TestComputeCarryover_EvenPeriod_CreatesNothing(t *testing.T) {
	rows := computeCarryover(0, map[uuid.UUID]int64{uuid.New(): dollars(2000)}, 0)
	assert.Empty(t, rows)
}

// Income 2000, spend 1500 → Savings $500, no method.
func TestComputeCarryover_Leftover_SingleSavingsRowWithNoMethod(t *testing.T) {
	method := uuid.New()

	rows := computeCarryover(dollars(500), map[uuid.UUID]int64{method: dollars(1500)}, 0)

	require.Len(t, rows, 1)
	assert.Equal(t, dollars(500), rows[0].amountNanos)
	assert.Equal(t, carryoverCategorySavings, rows[0].categoryName)
	// A surplus is owed to nobody, so it must not be pinned on the method the
	// money happened to be spent through.
	assert.Nil(t, rows[0].paymentMethodID)
}

// Income 2000, spend 2300 on one method → Debt $300 on that method.
func TestComputeCarryover_Shortfall_SingleMethodTakesAll(t *testing.T) {
	method := uuid.New()

	rows := computeCarryover(-dollars(300), map[uuid.UUID]int64{method: dollars(2300)}, 0)

	require.Len(t, rows, 1)
	assert.Equal(t, dollars(300), rows[0].amountNanos)
	assert.Equal(t, carryoverCategoryDebt, rows[0].categoryName)
	require.NotNil(t, rows[0].paymentMethodID)
	assert.Equal(t, method, *rows[0].paymentMethodID)
}

// Income 2000: 1800 debit + 500 Chase Visa → Debt $234.78 debit, $65.22 Visa.
// The exact figures matter: flooring both shares would give $234.78 + $65.21
// and lose a cent against the balance the user was shown.
func TestComputeCarryover_Shortfall_SplitProportionallyAcrossMethods(t *testing.T) {
	debit := uuid.New()
	visa := uuid.New()

	rows := computeCarryover(-dollars(300), map[uuid.UUID]int64{
		debit: dollars(1800),
		visa:  dollars(500),
	}, 0)

	require.Len(t, rows, 2)
	byMethod := map[uuid.UUID]int64{}
	for _, r := range rows {
		require.NotNil(t, r.paymentMethodID)
		assert.Equal(t, carryoverCategoryDebt, r.categoryName)
		byMethod[*r.paymentMethodID] = r.amountNanos
	}
	assert.Equal(t, dollars(234.78), byMethod[debit])
	assert.Equal(t, dollars(65.22), byMethod[visa])
	assert.Equal(t, dollars(300), sumRows(rows))
}

// Income 2000: 2100 debit + 200 with no payment method →
// Debt $273.91 debit, $26.09 no method.
func TestComputeCarryover_Shortfall_UnattributedSpendTakesItsShare(t *testing.T) {
	debit := uuid.New()

	rows := computeCarryover(-dollars(300), map[uuid.UUID]int64{
		debit: dollars(2100),
	}, dollars(200))

	require.Len(t, rows, 2)
	var debitAmount, unattributedAmount int64
	for _, r := range rows {
		if r.paymentMethodID == nil {
			unattributedAmount = r.amountNanos
			continue
		}
		assert.Equal(t, debit, *r.paymentMethodID)
		debitAmount = r.amountNanos
	}
	assert.Equal(t, dollars(273.91), debitAmount)
	assert.Equal(t, dollars(26.09), unattributedAmount)
	assert.Equal(t, dollars(300), sumRows(rows))
}

// Three-way split: the rounding leftovers have to be handed out without ever
// losing or inventing a cent.
func TestComputeCarryover_Shortfall_ThreeMethodsStillSumExactly(t *testing.T) {
	a, b, c := uuid.New(), uuid.New(), uuid.New()

	rows := computeCarryover(-dollars(300), map[uuid.UUID]int64{
		a: dollars(1000),
		b: dollars(800),
		c: dollars(500),
	}, 0)

	require.Len(t, rows, 3)
	byMethod := map[uuid.UUID]int64{}
	for _, r := range rows {
		byMethod[*r.paymentMethodID] = r.amountNanos
	}
	assert.Equal(t, dollars(130.43), byMethod[a])
	assert.Equal(t, dollars(104.35), byMethod[b])
	assert.Equal(t, dollars(65.22), byMethod[c])
	assert.Equal(t, dollars(300), sumRows(rows))
}

// A method with more received on it than spent didn't cause the overspend, so
// it must not be handed part of the debt.
func TestComputeCarryover_Shortfall_IgnoresMethodsWithNoNetSpend(t *testing.T) {
	spender := uuid.New()
	refunded := uuid.New()

	rows := computeCarryover(-dollars(300), map[uuid.UUID]int64{
		spender:  dollars(2300),
		refunded: -dollars(50),
	}, 0)

	require.Len(t, rows, 1)
	require.NotNil(t, rows[0].paymentMethodID)
	assert.Equal(t, spender, *rows[0].paymentMethodID)
	assert.Equal(t, dollars(300), rows[0].amountNanos)
}

// Every method zeroed out and a shortfall still on the books: attribute it to
// nothing rather than dropping it, since a silently vanishing balance is the
// bug this feature exists to fix.
func TestComputeCarryover_Shortfall_NoSpendFallsBackToUnattributedRow(t *testing.T) {
	rows := computeCarryover(-dollars(300), map[uuid.UUID]int64{}, 0)

	require.Len(t, rows, 1)
	assert.Nil(t, rows[0].paymentMethodID)
	assert.Equal(t, carryoverCategoryDebt, rows[0].categoryName)
	assert.Equal(t, dollars(300), rows[0].amountNanos)
}

// The amount column is NUMERIC(15,4), so a balance need not land on a whole
// cent. The odd fraction has to survive the split.
func TestComputeCarryover_Shortfall_SubCentRemainderIsNotLost(t *testing.T) {
	a, b := uuid.New(), uuid.New()
	shortfall := dollars(300) + 5_000_000 // $300.005

	rows := computeCarryover(-shortfall, map[uuid.UUID]int64{
		a: dollars(1800),
		b: dollars(500),
	}, 0)

	require.Len(t, rows, 2)
	assert.Equal(t, shortfall, sumRows(rows))
}

// Map iteration order is randomised, so the same input must not be able to
// produce two different splits across runs.
func TestComputeCarryover_Shortfall_IsDeterministic(t *testing.T) {
	a, b, c := uuid.New(), uuid.New(), uuid.New()
	spend := map[uuid.UUID]int64{a: dollars(1000), b: dollars(1000), c: dollars(300)}

	first := computeCarryover(-dollars(300), spend, 0)
	for i := 0; i < 25; i++ {
		got := computeCarryover(-dollars(300), spend, 0)
		assert.Equal(t, first, got, "split changed between runs on identical input")
	}
}

func TestNumericFromNanos_RoundTripsExactly(t *testing.T) {
	for _, want := range []int64{0, 1, dollars(0.01), dollars(234.78), -dollars(65.22)} {
		assert.Equal(t, want, numericToNanos(numericFromNanos(want)))
	}
}
