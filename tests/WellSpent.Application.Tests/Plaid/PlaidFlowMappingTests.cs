using WellSpent.Application.Plaid;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Plaid;

public sealed class PlaidFlowMappingTests
{
    [Theory]
    [InlineData("MOBILE PAYMENT - THANK YOU", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", TransactionFlow.Payment)]
    [InlineData("ACME CORP DIRECT DEP", "INCOME", "INCOME_WAGES", TransactionFlow.Income)]
    [InlineData("IRS TREAS 310 TAX REF", "INCOME", "INCOME_TAX_REFUND", TransactionFlow.Income)]
    [InlineData("Zelle to Jordan", "TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER", TransactionFlow.Transfer)]
    [InlineData("Venmo from Alex", "TRANSFER_IN", "TRANSFER_IN_ACCOUNT_TRANSFER", TransactionFlow.Transfer)]
    [InlineData("ACME CORP PAYROLL", "TRANSFER_IN", "TRANSFER_IN_DEPOSIT", TransactionFlow.Income)] // payroll in the name wins
    [InlineData("payroll deposit", "", "", TransactionFlow.Income)]
    public void Resolve_FromPlaidClassification(string name, string primary, string detailed, string expected) =>
        Assert.Equal(expected, PlaidFlowMapping.Resolve(name, primary, detailed));

    [Theory]
    [InlineData("Car loan", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CAR_PAYMENT")] // loan payments are spending
    [InlineData("To savings", "TRANSFER_OUT", "TRANSFER_OUT_SAVINGS")] // saving, filed under Savings
    [InlineData("WHOLE FOODS", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES")]
    public void Resolve_OrdinarySpendingIsLeftToTheSign(string name, string primary, string detailed) =>
        Assert.Null(PlaidFlowMapping.Resolve(name, primary, detailed));
}
