package plaid

// plaidDetailedToCategory maps Plaid personal_finance_category.detailed values
// to SpendSense system category names. Only entries that differ from the
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

	// GENERAL_SERVICES — override the Services default for specific subtypes
	"GENERAL_SERVICES_INSURANCE":  "Insurance",
	"GENERAL_SERVICES_AUTOMOTIVE": "Auto", // oil changes, car washes, repairs, towing
	"GENERAL_SERVICES_CHILDCARE":  "Baby", // babysitters, daycare

	// TRANSPORTATION — only gas and tolls map to Gas; everything else is Misc
	"TRANSPORTATION_GAS":   "Gas",
	"TRANSPORTATION_TOLLS": "Gas",

	// LOAN_PAYMENTS / LOAN_DISBURSEMENTS — credit card payments seen from both
	// sides of the ledger (outflow from checking, inflow on the card account)
	"LOAN_PAYMENTS_CREDIT_CARD_PAYMENT":      "Payment",
	"LOAN_DISBURSEMENTS_OTHER_DISBURSEMENT": "Payment",
}

// plaidPrimaryToCategory maps Plaid personal_finance_category.primary values
// to SpendSense system category names. Used as a fallback when the detailed
// key has no specific override.
var plaidPrimaryToCategory = map[string]string{
	"FOOD_AND_DRINK":            "Eating Out",
	"GENERAL_MERCHANDISE":       "Shopping",
	"HOME_IMPROVEMENT":          "House",
	"MEDICAL":                   "Wellness",
	"PERSONAL_CARE":             "Wellness",
	"GENERAL_SERVICES":          "Services",
	"TRANSPORTATION":            "Misc",
	"TRAVEL":                    "Travel",
	"RENT_AND_UTILITIES":        "Rent",
	"ENTERTAINMENT":             "Entertainment",
	"BANK_FEES":                 "Misc",
	"GOVERNMENT_AND_NON_PROFIT": "Misc",
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
	// single category covers both sides.
	"TRANSFER_IN":  "Transfer",
	"TRANSFER_OUT": "Transfer",
	// LOAN_PAYMENTS primary is left unmapped — specific subtypes (credit card
	// payments) are handled in plaidDetailedToCategory above. Other loan
	// payments (student loans, car loans) fall through to uncategorized rather
	// than being guessed at.
}

// ResolvePlaidCategory returns the SpendSense system category name for a Plaid
// transaction. Detailed is checked first, primary is the fallback. Returns ""
// when no mapping exists (transaction will be imported uncategorized).
func ResolvePlaidCategory(primary, detailed string) string {
	if cat, ok := plaidDetailedToCategory[detailed]; ok {
		return cat
	}
	return plaidPrimaryToCategory[primary]
}
