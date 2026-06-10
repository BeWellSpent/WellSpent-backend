package handler

import (
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
