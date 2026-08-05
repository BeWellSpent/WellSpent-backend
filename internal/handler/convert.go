package handler

import (
	"fmt"
	"math/big"

	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"google.golang.org/protobuf/types/known/timestamppb"
)

// moneyFromNumeric converts a pgtype.Numeric (NUMERIC(12,2)) to a proto Money.
// Amounts in this app are at most 2 decimal places, so nanos are always a multiple of 10^7.
//
// This must stay exact integer arithmetic, never a float64 intermediate: most
// cents amounts (e.g. 19.99) aren't exactly representable in binary floating
// point, so converting through float64 can produce a nanos value off by one
// (e.g. 989999999 instead of 990000000). A client that reads that value and
// echoes it back unchanged (e.g. a category-only edit on a locked
// transaction, which must resend amount/planned_amount verbatim) would then
// fail assertOnlyCategoryChanged's exact-equality check against the
// unmodified DB row — a real "amount changed" mismatch caused entirely by
// this conversion, not by anything the client did.
func moneyFromNumeric(n pgtype.Numeric) *v1.Money {
	if !n.Valid {
		return &v1.Money{}
	}
	// Scale the exact decimal value (n.Int * 10^n.Exp) to nanos (10^-9) via
	// big.Int only. This app's amounts never exceed 4 decimal places, so
	// nanoExp (n.Exp + 9) is always >= 0 in practice, making the scaling an
	// exact multiplication; the division branch is kept only as a safe
	// fallback (truncating, same as the multiplication path's implicit
	// exactness) should that invariant ever not hold.
	nanoExp := n.Exp + 9
	totalNanos := new(big.Int).Set(n.Int)
	if nanoExp > 0 {
		scale := new(big.Int).Exp(big.NewInt(10), big.NewInt(int64(nanoExp)), nil)
		totalNanos.Mul(totalNanos, scale)
	} else if nanoExp < 0 {
		scale := new(big.Int).Exp(big.NewInt(10), big.NewInt(int64(-nanoExp)), nil)
		totalNanos.Quo(totalNanos, scale)
	}

	billion := big.NewInt(1e9)
	unitsBig := new(big.Int).Quo(totalNanos, billion)
	nanosBig := new(big.Int).Rem(totalNanos, billion)
	return &v1.Money{Units: unitsBig.Int64(), Nanos: int32(nanosBig.Int64())}
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
