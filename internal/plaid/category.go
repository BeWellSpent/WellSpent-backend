package plaid

// plaidDetailedToCategory maps Plaid personal_finance_category.detailed values
// to WellSpent system category names. Only entries that differ from the
// primary-level default are listed here; everything else falls through to
// plaidPrimaryToCategory.
var plaidDetailedToCategory = map[string]string{
	// FOOD_AND_DRINK — split groceries out of the generic "Eating Out" default
	"FOOD_AND_DRINK_GROCERIES": "Groceries",

	// GENERAL_MERCHANDISE — pet supplies break out of generic Shopping
	"GENERAL_MERCHANDISE_PET_SUPPLIES": "Pet",

	// MEDICAL — vet bills belong with Pet, not Wellness
	"MEDICAL_VETERINARY_SERVICES": "Pet",

	// PERSONAL_CARE — laundry/dry cleaning is a household service, not personal care
	"PERSONAL_CARE_LAUNDRY_AND_DRY_CLEANING": "Services",

	// GENERAL_SERVICES — override the Services default for specific subtypes.
	// Childcare stays on Baby rather than Family: daycare and babysitters are
	// specifically a young-child cost, and Family is the broader manual-pick
	// category users apply themselves.
	"GENERAL_SERVICES_INSURANCE":  "Insurance",
	"GENERAL_SERVICES_AUTOMOTIVE": "Auto", // oil changes, car washes, repairs, towing
	"GENERAL_SERVICES_CHILDCARE":  "Baby", // babysitters, daycare

	// TRANSPORTATION — fuel and tolls are car-running costs, so they stay on
	// Gas; everything else (transit, ride shares, parking, bikes) is
	// Transportation via the primary default.
	"TRANSPORTATION_GAS":   "Gas",
	"TRANSPORTATION_TOLLS": "Gas",

	// RENT_AND_UTILITIES — the primary defaults to Utilities, so only rent
	// itself needs pulling back out. Done this way round rather than listing
	// all six utility subtypes: if Plaid adds a new one, defaulting it to
	// Utilities is far more likely right than defaulting it to Rent.
	"RENT_AND_UTILITIES_RENT": "Rent",

	// TRANSFER_IN / TRANSFER_OUT — money moved to or from a savings account is
	// saving, not a generic transfer. Direction is carried by the amount sign.
	"TRANSFER_IN_SAVINGS":  "Savings",
	"TRANSFER_OUT_SAVINGS": "Savings",

	// ENTERTAINMENT — streaming services are the recurring-subscription case
	// the Subscription category exists for.
	"ENTERTAINMENT_TV_AND_MOVIES": "Subscription",

	// LOAN_PAYMENTS — paying off a credit card isn't debt service in the
	// budgeting sense, it's settling a balance already counted when it was
	// spent. Every other loan payment falls through to Loan.
	"LOAN_PAYMENTS_CREDIT_CARD_PAYMENT": "Payment",
}

// plaidPrimaryToCategory maps Plaid personal_finance_category.primary values
// to WellSpent system category names. Used as a fallback when the detailed
// key has no specific override.
//
// All 16 primaries in Plaid's taxonomy are covered, so an unrecognized
// detailed value can never import uncategorized.
var plaidPrimaryToCategory = map[string]string{
	"FOOD_AND_DRINK":            "Eating Out",
	"GENERAL_MERCHANDISE":       "Shopping",
	"HOME_IMPROVEMENT":          "House",
	"MEDICAL":                   "Wellness",
	"PERSONAL_CARE":             "Wellness",
	"GENERAL_SERVICES":          "Services",
	"TRANSPORTATION":            "Transportation",
	"TRAVEL":                    "Travel",
	"RENT_AND_UTILITIES":        "Utilities",
	"ENTERTAINMENT":             "Entertainment",
	"BANK_FEES":                 "Misc",
	"GOVERNMENT_AND_NON_PROFIT": "Misc",
	// Mortgages, car loans, student loans and personal loans are all debt
	// service and belong together. Credit card payments are the one exception
	// (see plaidDetailedToCategory above).
	"LOAN_PAYMENTS": "Loan",
	// INCOME covers Plaid's own wages/dividends/interest/refund/etc.
	// classification — mapping it here means a payroll deposit is recognized
	// as Income from Plaid's PFC data alone, without depending on the
	// transaction name containing the literal word "payroll" (see
	// syncResolveCategory in internal/service/plaid_sync.go for that
	// name-based override, kept as a fallback for accounts where Plaid
	// doesn't return personal_finance_category data at all).
	"INCOME": "Income",
	// TRANSFER_IN / TRANSFER_OUT map to the Transfer category. Direction
	// (inbound vs outbound) is captured by positive/negative amount, so a
	// single category covers both sides. Savings transfers are split out
	// above.
	"TRANSFER_IN":  "Transfer",
	"TRANSFER_OUT": "Transfer",
}

// ResolvePlaidCategory returns the WellSpent system category name for a Plaid
// transaction. Detailed is checked first, primary is the fallback. Returns ""
// only for a primary outside Plaid's published taxonomy.
func ResolvePlaidCategory(primary, detailed string) string {
	if cat, ok := plaidDetailedToCategory[detailed]; ok {
		return cat
	}
	return plaidPrimaryToCategory[primary]
}
