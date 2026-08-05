package handler

import (
	"math/big"
	"testing"

	"github.com/jackc/pgx/v5/pgtype"
)

// Covers amounts that don't round-trip exactly through float64 (e.g. 19.99).
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

// Confirms reading an amount and resending it unchanged reconstructs the identical NUMERIC value.
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

		origF, _ := original.Float64Value()
		rtF, _ := roundTripped.Float64Value()
		if origF.Float64 != rtF.Float64 {
			t.Fatalf("round-trip mismatch for %d*10^%d: original=%v roundTripped=%v (money units=%d nanos=%d)",
				a.unscaled, a.exp, origF.Float64, rtF.Float64, money.Units, money.Nanos)
		}
	}
}
