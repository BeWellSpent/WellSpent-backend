using WellSpent.Application.Plaid;
using Xunit;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/plaid/category_test.go's TestResolvePlaidCategory_* suite exactly, against the system_key string convention instead of Go's category.Key type.</summary>
public sealed class PlaidCategoryMappingTests
{
    // Plaid's full PFC taxonomy at the primary level — proves no primary falls through uncategorized.
    private static readonly string[] PlaidPrimaries =
    [
        "INCOME", "TRANSFER_IN", "TRANSFER_OUT", "LOAN_PAYMENTS", "BANK_FEES",
        "ENTERTAINMENT", "FOOD_AND_DRINK", "GENERAL_MERCHANDISE", "HOME_IMPROVEMENT",
        "MEDICAL", "PERSONAL_CARE", "GENERAL_SERVICES", "GOVERNMENT_AND_NON_PROFIT",
        "TRANSPORTATION", "TRAVEL", "RENT_AND_UTILITIES",
    ];

    [Fact]
    public void EveryPlaidPrimaryResolvesToACategoryOrAFlow()
    {
        foreach (var primary in PlaidPrimaries)
        {
            // An unrecognized detailed value under a known primary must still land somewhere.
            var detailed = primary + "_SOMETHING_NEW";
            var category = PlaidCategoryMapping.Resolve(primary, detailed);
            var flow = PlaidFlowMapping.Resolve("", primary, detailed);
            Assert.True(category.Length > 0 || flow is not null, $"{primary} has neither a category nor a flow");
        }
    }

    [Fact]
    public void IncomeAndTransfersHaveNoCategory() // their meaning is the flow (#119)
    {
        Assert.Equal("", PlaidCategoryMapping.Resolve("INCOME", "INCOME_WAGES"));
        Assert.Equal("", PlaidCategoryMapping.Resolve("TRANSFER_IN", "TRANSFER_IN_ACCOUNT_TRANSFER"));
        Assert.Equal("", PlaidCategoryMapping.Resolve("TRANSFER_OUT", "TRANSFER_OUT_ACCOUNT_TRANSFER"));
    }

    [Fact]
    public void SavingsTransfersSplitOutOfTransfer()
    {
        Assert.Equal("savings", PlaidCategoryMapping.Resolve("TRANSFER_IN", "TRANSFER_IN_SAVINGS"));
        Assert.Equal("savings", PlaidCategoryMapping.Resolve("TRANSFER_OUT", "TRANSFER_OUT_SAVINGS"));
    }

    [Fact]
    public void CreditCardPaymentsHaveNoCategory_NotLoan() =>
        Assert.Equal("", PlaidCategoryMapping.Resolve("LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT"));

    [Theory]
    [InlineData("LOAN_PAYMENTS_CAR_PAYMENT")]
    [InlineData("LOAN_PAYMENTS_MORTGAGE_PAYMENT")]
    [InlineData("LOAN_PAYMENTS_STUDENT_LOAN_PAYMENT")]
    [InlineData("LOAN_PAYMENTS_PERSONAL_LOAN_PAYMENT")]
    [InlineData("LOAN_PAYMENTS_OTHER_PAYMENT")]
    public void OtherLoanPaymentsMappedToLoan(string detailed)
    {
        Assert.Equal("loan", PlaidCategoryMapping.Resolve("LOAN_PAYMENTS", detailed));
    }

    [Theory]
    [InlineData("TRANSPORTATION_PUBLIC_TRANSIT")]
    [InlineData("TRANSPORTATION_TAXIS_AND_RIDE_SHARES")]
    [InlineData("TRANSPORTATION_PARKING")]
    [InlineData("TRANSPORTATION_BIKES_AND_SCOOTERS")]
    [InlineData("TRANSPORTATION_OTHER_TRANSPORTATION")]
    public void TransportationNoLongerFallsIntoMisc(string detailed)
    {
        Assert.Equal("transportation", PlaidCategoryMapping.Resolve("TRANSPORTATION", detailed));
    }

    [Fact]
    public void TransportationGasAndTollsStayWithCarRunningCosts()
    {
        Assert.Equal("gas", PlaidCategoryMapping.Resolve("TRANSPORTATION", "TRANSPORTATION_GAS"));
        Assert.Equal("gas", PlaidCategoryMapping.Resolve("TRANSPORTATION", "TRANSPORTATION_TOLLS"));
    }

    [Theory]
    [InlineData("RENT_AND_UTILITIES_GAS_AND_ELECTRICITY")]
    [InlineData("RENT_AND_UTILITIES_INTERNET_AND_CABLE")]
    [InlineData("RENT_AND_UTILITIES_TELEPHONE")]
    [InlineData("RENT_AND_UTILITIES_WATER")]
    [InlineData("RENT_AND_UTILITIES_SEWAGE_AND_WASTE_MANAGEMENT")]
    [InlineData("RENT_AND_UTILITIES_OTHER_UTILITIES")]
    public void UtilitiesSplitFromRent(string detailed)
    {
        Assert.Equal("utilities", PlaidCategoryMapping.Resolve("RENT_AND_UTILITIES", detailed));
    }

    [Fact]
    public void RentAndUtilitiesRentStaysRent()
    {
        Assert.Equal("rent", PlaidCategoryMapping.Resolve("RENT_AND_UTILITIES", "RENT_AND_UTILITIES_RENT"));
    }

    [Fact]
    public void StreamingMappedToSubscription()
    {
        Assert.Equal("subscription", PlaidCategoryMapping.Resolve("ENTERTAINMENT", "ENTERTAINMENT_TV_AND_MOVIES"));
        // Other entertainment stays put.
        Assert.Equal("entertainment", PlaidCategoryMapping.Resolve("ENTERTAINMENT", "ENTERTAINMENT_VIDEO_GAMES"));
    }

    [Fact]
    public void DetailedOverridesPrimary()
    {
        Assert.Equal("groceries", PlaidCategoryMapping.Resolve("FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"));
    }

    [Fact]
    public void UnknownReturnsEmpty()
    {
        Assert.Equal("", PlaidCategoryMapping.Resolve("SOMETHING_NEW", "SOMETHING_NEW_SUBTYPE"));
    }
}
