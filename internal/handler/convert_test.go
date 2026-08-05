package handler

import (
	"math/big"
	"testing"

	"github.com/jackc/pgx/v5/pgtype"
)

// TestMoneyFromNumeric_RoundTripsExactly_ForAmountsProneToFloatRounding covers
// dollar amounts whose decimal value isn't exactly representable in binary
// floating point (e.g. 19.99). The old implementation converted through a
// float64 intermediate, which could produce a nanos value off by one
// (989999999 instead of 990000000) — a real, silent precision loss that made
// a category-only edit on a locked transaction fail the backend's exact
// equality check, since resending the read value unchanged no longer matched
// what was actually stored.
func TestMoneyFromNumeric_RoundTripsExactly_ForAmountsProneToFloatRounding(t *testing.T) {
	cases := []struct {
		name      string
		unscaled  int64 // NUMERIC unscaled integer value
		exp       int32 // NUMERIC exponent (value = unscaled * 10^exp)
		wantUnits int64
		wantNanos int32
	}{
		{"19.99", 199900, -4, 19, 990000000},
		{"33.33", 333300, -4, 33, 330000000},
		{"12.34", 123400, -4, 12, 340000000},
		{"9.10", 91000, -4, 9, 100000000},
		{"1234.56", 12345600, -4, 1234, 560000000},
		{"0.99", 9900, -4, 0, 990000000},
		{"100.01", 1000100, -4, 100, 10000000},
		{"45.67", 456700, -4, 45, 670000000},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			n := pgtype.Numeric{Int: big.NewInt(c.unscaled), Exp: c.exp, Valid: true}
			got := moneyFromNumeric(n)
			if got.Units != c.wantUnits || got.Nanos != c.wantNanos {
				t.Fatalf("moneyFromNumeric(%d * 10^%d) = {Units: %d, Nanos: %d}, want {Units: %d, Nanos: %d}",
					c.unscaled, c.exp, got.Units, got.Nanos, c.wantUnits, c.wantNanos)
			}
		})
	}
}

// TestMoneyFromNumeric_NumericFromMoney_RoundTrip confirms that reading a
// stored amount and immediately resending it unchanged (the exact locked-
// transaction category-only-edit path) reconstructs the identical NUMERIC
// value — the invariant assertOnlyCategoryChanged's equalNumeric relies on.
func TestMoneyFromNumeric_NumericFromMoney_RoundTrip(t *testing.T) {
	amounts := []struct {
		unscaled int64
		exp      int32
	}{
		{199900, -4}, {333300, -4}, {123400, -4}, {91000, -4},
		{12345600, -4}, {9900, -4}, {1000100, -4}, {456700, -4},
		{-199900, -4}, // negative (Received transaction)
	}
	for _, a := range amounts {
		original := pgtype.Numeric{Int: big.NewInt(a.unscaled), Exp: a.exp, Valid: true}
		money := moneyFromNumeric(original)
		roundTripped := numericFromMoney(money)

		// Mirrors internal/service.equalNumeric's own comparison — that
		// function lives in a different package, but a plain Float64Value
		// comparison is exactly what it does under the hood.
		origF, _ := original.Float64Value()
		rtF, _ := roundTripped.Float64Value()
		if origF.Float64 != rtF.Float64 {
			t.Fatalf("round-trip mismatch for %d*10^%d: original=%v roundTripped=%v (money units=%d nanos=%d)",
				a.unscaled, a.exp, origF.Float64, rtF.Float64, money.Units, money.Nanos)
		}
	}
}
