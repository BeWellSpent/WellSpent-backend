package plaid

import (
	"github.com/BeWellSpent/wellspent-backend/internal/category"
	"testing"

	"github.com/stretchr/testify/assert"
)

// plaidPrimaries is Plaid's full personal_finance_category taxonomy (v2) at
// the primary level, transcribed from their published CSV:
// https://plaid.com/documents/transactions-personal-finance-category-taxonomy.csv
//
// Kept here so TestResolvePlaidCategory_EveryPlaidPrimaryResolves can prove no
// primary falls through uncategorized. LOAN_PAYMENTS previously did, silently,
// which meant car, mortgage, student and personal loan payments imported with
// no category at all.
var plaidPrimaries = []string{
	"INCOME",
	"TRANSFER_IN",
	"TRANSFER_OUT",
	"LOAN_PAYMENTS",
	"BANK_FEES",
	"ENTERTAINMENT",
	"FOOD_AND_DRINK",
	"GENERAL_MERCHANDISE",
	"HOME_IMPROVEMENT",
	"MEDICAL",
	"PERSONAL_CARE",
	"GENERAL_SERVICES",
	"GOVERNMENT_AND_NON_PROFIT",
	"TRANSPORTATION",
	"TRAVEL",
	"RENT_AND_UTILITIES",
}

func TestResolvePlaidCategory_EveryPlaidPrimaryResolves(t *testing.T) {
	for _, primary := range plaidPrimaries {
		// An unrecognized detailed value under a known primary must still land
		// somewhere — this is the case Plaid creates whenever it extends the
		// taxonomy without us noticing.
		assert.NotEmptyf(t, ResolvePlaidCategory(primary, primary+"_SOMETHING_NEW"),
			"%s has no mapping — transactions under it would import uncategorized", primary)
	}
}

func TestResolvePlaidCategory_IncomePrimaryMapsToIncome(t *testing.T) {
	assert.Equal(t, category.Income, ResolvePlaidCategory("INCOME", "INCOME_WAGES"))
	assert.Equal(t, category.Income, ResolvePlaidCategory("INCOME", "INCOME_DIVIDENDS"))
	assert.Equal(t, category.Income, ResolvePlaidCategory("INCOME", "INCOME_OTHER_INCOME"))
}

func TestResolvePlaidCategory_TransfersMappedToTransfer(t *testing.T) {
	assert.Equal(t, category.Transfer, ResolvePlaidCategory("TRANSFER_IN", "TRANSFER_IN_ACCOUNT_TRANSFER"))
	assert.Equal(t, category.Transfer, ResolvePlaidCategory("TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER"))
}

func TestResolvePlaidCategory_SavingsTransfersSplitOutOfTransfer(t *testing.T) {
	assert.Equal(t, category.Savings, ResolvePlaidCategory("TRANSFER_IN", "TRANSFER_IN_SAVINGS"))
	assert.Equal(t, category.Savings, ResolvePlaidCategory("TRANSFER_OUT", "TRANSFER_OUT_SAVINGS"))
}

func TestResolvePlaidCategory_CreditCardPaymentsMappedToPayment(t *testing.T) {
	// Note there is deliberately no LOAN_DISBURSEMENTS case here. The map used
	// to carry LOAN_DISBURSEMENTS_OTHER_DISBURSEMENT and this test used to
	// assert it, but no LOAN_DISBURSEMENTS primary exists anywhere in Plaid's
	// taxonomy — the key never matched a real transaction, and the assertion
	// is what made it look covered.
	assert.Equal(t, category.Payment, ResolvePlaidCategory("LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT"))
}

func TestResolvePlaidCategory_OtherLoanPaymentsMappedToLoan(t *testing.T) {
	for _, detailed := range []string{
		"LOAN_PAYMENTS_CAR_PAYMENT",
		"LOAN_PAYMENTS_MORTGAGE_PAYMENT",
		"LOAN_PAYMENTS_STUDENT_LOAN_PAYMENT",
		"LOAN_PAYMENTS_PERSONAL_LOAN_PAYMENT",
		"LOAN_PAYMENTS_OTHER_PAYMENT",
	} {
		assert.Equalf(t, category.Loan, ResolvePlaidCategory("LOAN_PAYMENTS", detailed),
			"%s should be debt service", detailed)
	}
}

func TestResolvePlaidCategory_TransportationNoLongerFallsIntoMisc(t *testing.T) {
	for _, detailed := range []string{
		"TRANSPORTATION_PUBLIC_TRANSIT",
		"TRANSPORTATION_TAXIS_AND_RIDE_SHARES",
		"TRANSPORTATION_PARKING",
		"TRANSPORTATION_BIKES_AND_SCOOTERS",
		"TRANSPORTATION_OTHER_TRANSPORTATION",
	} {
		assert.Equalf(t, category.Transportation, ResolvePlaidCategory("TRANSPORTATION", detailed),
			"%s should be Transportation, not Misc", detailed)
	}
	// Fuel and tolls stay with the car-running costs.
	assert.Equal(t, category.Gas, ResolvePlaidCategory("TRANSPORTATION", "TRANSPORTATION_GAS"))
	assert.Equal(t, category.Gas, ResolvePlaidCategory("TRANSPORTATION", "TRANSPORTATION_TOLLS"))
}

func TestResolvePlaidCategory_UtilitiesSplitFromRent(t *testing.T) {
	for _, detailed := range []string{
		"RENT_AND_UTILITIES_GAS_AND_ELECTRICITY",
		"RENT_AND_UTILITIES_INTERNET_AND_CABLE",
		"RENT_AND_UTILITIES_TELEPHONE",
		"RENT_AND_UTILITIES_WATER",
		"RENT_AND_UTILITIES_SEWAGE_AND_WASTE_MANAGEMENT",
		"RENT_AND_UTILITIES_OTHER_UTILITIES",
	} {
		assert.Equalf(t, category.Utilities, ResolvePlaidCategory("RENT_AND_UTILITIES", detailed),
			"%s should be Utilities, not Rent", detailed)
	}
	assert.Equal(t, category.Rent, ResolvePlaidCategory("RENT_AND_UTILITIES", "RENT_AND_UTILITIES_RENT"))
}

func TestResolvePlaidCategory_StreamingMappedToSubscription(t *testing.T) {
	assert.Equal(t, category.Subscription, ResolvePlaidCategory("ENTERTAINMENT", "ENTERTAINMENT_TV_AND_MOVIES"))
	// Other entertainment stays put.
	assert.Equal(t, category.Entertainment, ResolvePlaidCategory("ENTERTAINMENT", "ENTERTAINMENT_VIDEO_GAMES"))
}

func TestResolvePlaidCategory_DetailedOverridesPrimary(t *testing.T) {
	assert.Equal(t, category.Groceries, ResolvePlaidCategory("FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"))
}

func TestResolvePlaidCategory_UnknownReturnsEmpty(t *testing.T) {
	assert.Equal(t, category.Key(""), ResolvePlaidCategory("SOMETHING_NEW", "SOMETHING_NEW_SUBTYPE"))
}
