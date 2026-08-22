package plaid

import "github.com/BeWellSpent/wellspent-backend/internal/category"

// plaidDetailedToCategory maps Plaid personal_finance_category.detailed values
// to WellSpent system category keys. Only entries that differ from the
// primary-level default are listed here; everything else falls through to
// plaidPrimaryToCategory.
var plaidDetailedToCategory = map[string]category.Key{
	// FOOD_AND_DRINK — split groceries out of the generic "Eating Out" default
	"FOOD_AND_DRINK_GROCERIES": category.Groceries,

	// GENERAL_MERCHANDISE — pet supplies break out of generic Shopping
	"GENERAL_MERCHANDISE_PET_SUPPLIES": category.Pet,

	// MEDICAL — vet bills belong with Pet, not Wellness
	"MEDICAL_VETERINARY_SERVICES": category.Pet,

	// PERSONAL_CARE — laundry/dry cleaning is a household service, not personal care
	"PERSONAL_CARE_LAUNDRY_AND_DRY_CLEANING": category.Services,

	// GENERAL_SERVICES — override the Services default for specific subtypes.
	// Childcare stays on Baby rather than Family: daycare and babysitters are
	// specifically a young-child cost, and Family is the broader manual-pick
	// category users apply themselves.
	"GENERAL_SERVICES_INSURANCE":  category.Insurance,
	"GENERAL_SERVICES_AUTOMOTIVE": category.Auto, // oil changes, car washes, repairs, towing
	"GENERAL_SERVICES_CHILDCARE":  category.Baby, // babysitters, daycare

	// TRANSPORTATION — fuel and tolls are car-running costs, so they stay on
	// Gas; everything else (transit, ride shares, parking, bikes) is
	// Transportation via the primary default.
	"TRANSPORTATION_GAS":   category.Gas,
	"TRANSPORTATION_TOLLS": category.Gas,

	// RENT_AND_UTILITIES — the primary defaults to Utilities, so only rent
	// itself needs pulling back out. Done this way round rather than listing
	// all six utility subtypes: if Plaid adds a new one, defaulting it to
	// Utilities is far more likely right than defaulting it to Rent.
	"RENT_AND_UTILITIES_RENT": category.Rent,

	// TRANSFER_IN / TRANSFER_OUT — money moved to or from a savings account is
	// saving, not a generic transfer. Direction is carried by the amount sign.
	"TRANSFER_IN_SAVINGS":  category.Savings,
	"TRANSFER_OUT_SAVINGS": category.Savings,

	// ENTERTAINMENT — streaming services are the recurring-subscription case
	// the Subscription category exists for.
	"ENTERTAINMENT_TV_AND_MOVIES": category.Subscription,

	// LOAN_PAYMENTS — paying off a credit card isn't debt service in the
	// budgeting sense, it's settling a balance already counted when it was
	// spent. Every other loan payment falls through to Loan.
	"LOAN_PAYMENTS_CREDIT_CARD_PAYMENT": category.Payment,
}

// plaidPrimaryToCategory maps Plaid personal_finance_category.primary values
// to WellSpent system category keys. Used as a fallback when the detailed
// key has no specific override.
//
// All 16 primaries in Plaid's taxonomy are covered, so an unrecognized
// detailed value can never import uncategorized.
var plaidPrimaryToCategory = map[string]category.Key{
	"FOOD_AND_DRINK":            category.EatingOut,
	"GENERAL_MERCHANDISE":       category.Shopping,
	"HOME_IMPROVEMENT":          category.House,
	"MEDICAL":                   category.Wellness,
	"PERSONAL_CARE":             category.Wellness,
	"GENERAL_SERVICES":          category.Services,
	"TRANSPORTATION":            category.Transportation,
	"TRAVEL":                    category.Travel,
	"RENT_AND_UTILITIES":        category.Utilities,
	"ENTERTAINMENT":             category.Entertainment,
	"BANK_FEES":                 category.Misc,
	"GOVERNMENT_AND_NON_PROFIT": category.Misc,
	// Mortgages, car loans, student loans and personal loans are all debt
	// service and belong together. Credit card payments are the one exception
	// (see plaidDetailedToCategory above).
	"LOAN_PAYMENTS": category.Loan,
	// INCOME covers Plaid's own wages/dividends/interest/refund/etc.
	// classification — mapping it here means a payroll deposit is recognized
	// as Income from Plaid's PFC data alone, without depending on the
	// transaction name containing the literal word "payroll" (see
	// syncResolveCategory in internal/service/plaid_sync.go for that
	// name-based override, kept as a fallback for accounts where Plaid
	// doesn't return personal_finance_category data at all).
	"INCOME": category.Income,
	// TRANSFER_IN / TRANSFER_OUT map to the Transfer category. Direction
	// (inbound vs outbound) is captured by positive/negative amount, so a
	// single category covers both sides. Savings transfers are split out
	// above.
	"TRANSFER_IN":  category.Transfer,
	"TRANSFER_OUT": category.Transfer,
}

// ResolvePlaidCategory returns the WellSpent system category key for a Plaid
// transaction. Detailed is checked first, primary is the fallback. Returns ""
// only for a primary outside Plaid's published taxonomy.
func ResolvePlaidCategory(primary, detailed string) category.Key {
	if cat, ok := plaidDetailedToCategory[detailed]; ok {
		return cat
	}
	return plaidPrimaryToCategory[primary]
}
