package handler

import (
	"fmt"
	"math/big"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	v1 "github.com/mauro-afa91/spendsense/gen/spendsense/v1"
	"google.golang.org/protobuf/types/known/timestamppb"
)

// moneyFromNumeric converts a pgtype.Numeric (NUMERIC(12,2)) to a proto Money.
// Amounts in this app are at most 2 decimal places, so nanos are always a multiple of 10^7.
func moneyFromNumeric(n pgtype.Numeric) *v1.Money {
	if !n.Valid {
		return &v1.Money{}
	}
	// Reconstruct as big.Float then extract units/nanos
	rat := new(big.Rat).SetInt(n.Int)
	if n.Exp > 0 {
		mul := new(big.Int).Exp(big.NewInt(10), big.NewInt(int64(n.Exp)), nil)
		rat.Mul(rat, new(big.Rat).SetInt(mul))
	} else if n.Exp < 0 {
		div := new(big.Int).Exp(big.NewInt(10), big.NewInt(int64(-n.Exp)), nil)
		rat.Quo(rat, new(big.Rat).SetInt(div))
	}

	f, _ := rat.Float64()
	units := int64(f)
	nanos := int32((f - float64(units)) * 1e9)
	return &v1.Money{Units: units, Nanos: nanos}
}

// numericFromMoney converts a proto Money back to pgtype.Numeric.
func numericFromMoney(m *v1.Money) pgtype.Numeric {
	if m == nil {
		return pgtype.Numeric{}
	}
	// Reconstruct as (units * 10^9 + nanos) / 10^9
	total := big.NewInt(m.Units*1e9 + int64(m.Nanos))
	exp := int32(-9)
	return pgtype.Numeric{Int: total, Exp: exp, Valid: true}
}

// dateFromProtoTS converts a proto Timestamp to pgtype.Date (date only).
func dateFromProtoTS(ts *timestamppb.Timestamp) pgtype.Date {
	if ts == nil {
		return pgtype.Date{}
	}
	t := ts.AsTime()
	return pgtype.Date{Time: t, Valid: true}
}

// protoTSFromDate converts pgtype.Date to proto Timestamp (midnight UTC).
func protoTSFromDate(d pgtype.Date) *timestamppb.Timestamp {
	if !d.Valid {
		return nil
	}
	return timestamppb.New(d.Time)
}

func nullStr(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}

func nullUUID(u *uuid.UUID) string {
	if u == nil {
		return ""
	}
	return u.String()
}

// filingStatusToString converts a proto FilingStatus enum to its stored string form (the integer value).
func filingStatusToString(fs v1.FilingStatus) string {
	return fmt.Sprintf("%d", int32(fs))
}

// filingStatusFromString parses the stored string back to a proto FilingStatus enum.
func filingStatusFromString(s string) v1.FilingStatus {
	if s == "" {
		return v1.FilingStatus_FILING_STATUS_UNSPECIFIED
	}
	for _, fs := range []v1.FilingStatus{
		v1.FilingStatus_FILING_STATUS_SINGLE,
		v1.FilingStatus_FILING_STATUS_MARRIED_FILING_JOINTLY,
		v1.FilingStatus_FILING_STATUS_MARRIED_FILING_SEPARATELY,
		v1.FilingStatus_FILING_STATUS_HEAD_OF_HOUSEHOLD,
		v1.FilingStatus_FILING_STATUS_QUALIFYING_SURVIVING_SPOUSE,
	} {
		if fmt.Sprintf("%d", int32(fs)) == s {
			return fs
		}
	}
	return v1.FilingStatus_FILING_STATUS_UNSPECIFIED
}

// taxPaymentFrequencyFromProto converts a proto TaxPaymentFrequency enum to its int32 month value.
func taxPaymentFrequencyFromProto(t v1.TaxPaymentFrequency) int32 {
	return int32(t)
}
