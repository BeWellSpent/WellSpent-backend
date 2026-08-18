package service

import (
	"math/big"
	"time"

	"github.com/jackc/pgx/v5/pgtype"
)

// installmentAmount splits a purchase evenly across n payments, rounded to the
// nearest cent.
//
// A fixed_expense carries ONE planned_amount that every spawned payment
// inherits, so an uneven total cannot be balanced by a larger final payment the
// way a card issuer does it — the residue is structural, not a rounding bug.
// $1,000 over 3 becomes $333.33 x 3 = $999.99. Nearest-cent keeps that residue
// as small as it can be (under half a cent per payment); see
// docs/features/installment-plans.md.
//
// Computed on big.Int rather than through a float64, for the same reason
// moneyFromNumeric was rewritten in handler/convert.go: most cent amounts are
// not exactly representable in binary floating point, and a value that is one
// nano off round-trips as a genuinely different NUMERIC.
//
// The division is exact and rounds once. Scaling to cents and then dividing
// would round twice, compounding the error.
func installmentAmount(total pgtype.Numeric, n int32) pgtype.Numeric {
	if !total.Valid || total.Int == nil || n < 1 {
		return pgtype.Numeric{}
	}

	// value = Int * 10^Exp, and we want it expressed in cents: Int * 10^(Exp+2).
	num := new(big.Int).Set(total.Int)
	den := big.NewInt(int64(n))
	switch exp := total.Exp + 2; {
	case exp > 0:
		num.Mul(num, pow10(int32(exp)))
	case exp < 0:
		den.Mul(den, pow10(-exp))
	}

	return pgtype.Numeric{Int: divRoundHalf(num, den), Exp: -2, Valid: true}
}

func pow10(n int32) *big.Int {
	return new(big.Int).Exp(big.NewInt(10), big.NewInt(int64(n)), nil)
}

// divRoundHalf divides a by b, rounding halves away from zero — so a residue of
// exactly half a cent lands on the larger magnitude rather than silently
// favouring one direction. b is always positive here.
func divRoundHalf(a, b *big.Int) *big.Int {
	q, r := new(big.Int).QuoRem(a, b, new(big.Int))
	if r.Sign() == 0 {
		return q
	}
	// 2*|r| >= |b| means the remainder is at or past the halfway point.
	if new(big.Int).Lsh(new(big.Int).Abs(r), 1).Cmp(new(big.Int).Abs(b)) < 0 {
		return q
	}
	if a.Sign() < 0 {
		return q.Sub(q, big.NewInt(1))
	}
	return q.Add(q, big.NewInt(1))
}

// installmentEndDate is the date of the last payment: the first payment plus
// one month per remaining payment. It is what actually stops the plan —
// createNextPeriod deactivates a template once a period starts past its
// end_date — while total_payments stays informational
// (docs/features/fixed-expense-payment-plan.md).
//
// Deliberately month-based regardless of the budget's own cycle: a card
// installment is billed per statement month whether the user budgets weekly or
// monthly.
func installmentEndDate(firstPayment time.Time, totalPayments int32) time.Time {
	return firstPayment.AddDate(0, int(totalPayments-1), 0)
}
