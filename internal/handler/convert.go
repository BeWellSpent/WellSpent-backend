package handler

import (
	"fmt"
	"github.com/BeWellSpent/wellspent-backend/internal/category"
	"math/big"

	v1 "github.com/BeWellSpent/wellspent-backend/gen/wellspent/v1"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgtype"
	"google.golang.org/protobuf/types/known/timestamppb"
)

// moneyFromNumeric converts a pgtype.Numeric (NUMERIC(12,2)) to a proto Money.
// Uses exact big.Int arithmetic, never float64 — a float64 intermediate can
// round most cents amounts (e.g. 19.99) to a nanos value off by one, breaking
// exact-equality checks on a value that's read back unchanged later.
func moneyFromNumeric(n pgtype.Numeric) *v1.Money {
	if !n.Valid {
		return &v1.Money{}
	}
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

// systemCategoryByKey maps category.system_key to its wire enum. Kept as an
// explicit map rather than derived from the enum name so a rename on either
// side is a compile error here instead of a silent UNSPECIFIED — which would
// degrade to the English name on every client with nothing logged.
var systemCategoryByKey = map[category.Key]v1.SystemCategory{
	category.Entertainment:  v1.SystemCategory_SYSTEM_CATEGORY_ENTERTAINMENT,
	category.Insurance:      v1.SystemCategory_SYSTEM_CATEGORY_INSURANCE,
	category.Loan:           v1.SystemCategory_SYSTEM_CATEGORY_LOAN,
	category.Wellness:       v1.SystemCategory_SYSTEM_CATEGORY_WELLNESS,
	category.Services:       v1.SystemCategory_SYSTEM_CATEGORY_SERVICES,
	category.Subscription:   v1.SystemCategory_SYSTEM_CATEGORY_SUBSCRIPTION,
	category.Rent:           v1.SystemCategory_SYSTEM_CATEGORY_RENT,
	category.Travel:         v1.SystemCategory_SYSTEM_CATEGORY_TRAVEL,
	category.EatingOut:      v1.SystemCategory_SYSTEM_CATEGORY_EATING_OUT,
	category.Groceries:      v1.SystemCategory_SYSTEM_CATEGORY_GROCERIES,
	category.Baby:           v1.SystemCategory_SYSTEM_CATEGORY_BABY,
	category.Pet:            v1.SystemCategory_SYSTEM_CATEGORY_PET,
	category.Misc:           v1.SystemCategory_SYSTEM_CATEGORY_MISC,
	category.House:          v1.SystemCategory_SYSTEM_CATEGORY_HOUSE,
	category.Gas:            v1.SystemCategory_SYSTEM_CATEGORY_GAS,
	category.Auto:           v1.SystemCategory_SYSTEM_CATEGORY_AUTO,
	category.Savings:        v1.SystemCategory_SYSTEM_CATEGORY_SAVINGS,
	category.Shopping:       v1.SystemCategory_SYSTEM_CATEGORY_SHOPPING,
	category.Family:         v1.SystemCategory_SYSTEM_CATEGORY_FAMILY,
	category.Income:         v1.SystemCategory_SYSTEM_CATEGORY_INCOME,
	category.Payment:        v1.SystemCategory_SYSTEM_CATEGORY_PAYMENT,
	category.Transfer:       v1.SystemCategory_SYSTEM_CATEGORY_TRANSFER,
	category.Transportation: v1.SystemCategory_SYSTEM_CATEGORY_TRANSPORTATION,
	category.Utilities:      v1.SystemCategory_SYSTEM_CATEGORY_UTILITIES,
	category.Debt:           v1.SystemCategory_SYSTEM_CATEGORY_DEBT,
}

// protoSystemCategory maps a nullable system_key to the wire enum. UNSPECIFIED
// for a user-created category (key is NULL) and for a key this build doesn't
// know, which is the same thing from a client's point of view: fall back to
// name.
func protoSystemCategory(systemKey *string) v1.SystemCategory {
	if systemKey == nil {
		return v1.SystemCategory_SYSTEM_CATEGORY_UNSPECIFIED
	}
	return systemCategoryByKey[category.Key(*systemKey)]
}

// toProtoCategory builds the wire Category. One helper rather than three
// inline literals: every category-returning RPC has to set system_category,
// and three separate literals is how one of them gets missed.
//
// Name is always the English value. It is the fallback a client renders when
// it doesn't recognise system_category, so it stays populated even for system
// categories the client will translate itself.
func toProtoCategory(id int32, name string, isSystem bool, color string, systemKey *string) *v1.Category {
	return &v1.Category{
		Id:             id,
		Name:           name,
		IsSystem:       isSystem,
		Color:          color,
		SystemCategory: protoSystemCategory(systemKey),
	}
}
