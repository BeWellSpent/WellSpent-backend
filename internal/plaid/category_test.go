package plaid

import (
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestResolvePlaidCategory_IncomePrimaryMapsToIncome(t *testing.T) {
	assert.Equal(t, "Income", ResolvePlaidCategory("INCOME", "INCOME_WAGES"))
	assert.Equal(t, "Income", ResolvePlaidCategory("INCOME", "INCOME_DIVIDENDS"))
	assert.Equal(t, "Income", ResolvePlaidCategory("INCOME", "INCOME_OTHER_INCOME"))
}

func TestResolvePlaidCategory_TransfersMappedToTransfer(t *testing.T) {
	assert.Equal(t, "Transfer", ResolvePlaidCategory("TRANSFER_IN", "TRANSFER_IN_ACCOUNT_TRANSFER"))
	assert.Equal(t, "Transfer", ResolvePlaidCategory("TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER"))
}

func TestResolvePlaidCategory_CreditCardPaymentsMappedToPayment(t *testing.T) {
	// Outflow from checking account
	assert.Equal(t, "Payment", ResolvePlaidCategory("LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT"))
	// Inflow on the credit card account ("Payment Thank You" entries)
	assert.Equal(t, "Payment", ResolvePlaidCategory("LOAN_DISBURSEMENTS", "LOAN_DISBURSEMENTS_OTHER_DISBURSEMENT"))
}

func TestResolvePlaidCategory_DetailedOverridesPrimary(t *testing.T) {
	assert.Equal(t, "Groceries", ResolvePlaidCategory("FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"))
}

func TestResolvePlaidCategory_UnknownReturnsEmpty(t *testing.T) {
	assert.Equal(t, "", ResolvePlaidCategory("SOMETHING_NEW", "SOMETHING_NEW_SUBTYPE"))
}
