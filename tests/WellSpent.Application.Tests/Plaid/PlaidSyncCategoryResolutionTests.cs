using WellSpent.Application.Plaid;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_sync_test.go's TestSyncResolveCategory_*/TestSyncResolveCategoryID_* suite.</summary>
public sealed class PlaidSyncCategoryResolutionTests
{
    [Fact]
    public void NonPayrollFallsBackToPfcMapping()
    {
        Assert.Equal("groceries", PlaidSyncCategoryResolution.ResolveKey("WHOLE FOODS", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"));
        Assert.Equal("", PlaidSyncCategoryResolution.ResolveKey("UNKNOWN MERCHANT", "", ""));
    }

    [Fact]
    public void ResolveId_ResolvesToKnownId()
    {
        var categoryIds = new Dictionary<string, int> { ["shopping"] = 7 };
        var (key, id) = PlaidSyncCategoryResolution.ResolveId("AMAZON.COM", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_ONLINE_MARKETPLACES", categoryIds);

        Assert.Equal("shopping", key);
        Assert.Equal(7, id);
    }

    [Fact]
    public void ResolveId_UnmappedNameReturnsNullId()
    {
        // Resolves to Shopping but the map doesn't have it — imports with category_id NULL.
        var categoryIds = new Dictionary<string, int> { ["groceries"] = 3 };
        var (key, id) = PlaidSyncCategoryResolution.ResolveId("AMAZON.COM", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_ONLINE_MARKETPLACES", categoryIds);

        Assert.Equal("shopping", key);
        Assert.Null(id);
    }

    [Fact]
    public void ResolveId_NoResolvedKeyReturnsEmpty()
    {
        var (key, id) = PlaidSyncCategoryResolution.ResolveId("UNKNOWN MERCHANT", "", "", new Dictionary<string, int>());

        Assert.Equal("", key);
        Assert.Null(id);
    }

    [Fact]
    public void ResolveId_RealCreditCardPaymentPayload_HasNoCategory()
    {
        // Exact field values from a real plaid.http.response: "MOBILE PAYMENT - THANK YOU",
        // LOAN_PAYMENTS / LOAN_PAYMENTS_CREDIT_CARD_PAYMENT. Its flow (payment) says what it is.
        var (key, id) = PlaidSyncCategoryResolution.ResolveId(
            "MOBILE PAYMENT - THANK YOU", "LOAN_PAYMENTS", "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", new Dictionary<string, int> { ["loan"] = 9 });

        Assert.Equal("", key);
        Assert.Null(id);
    }
}
