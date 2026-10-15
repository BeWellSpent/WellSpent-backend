using WellSpent.Application.Plaid;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_sync_test.go's TestSyncResolveCategory_*/TestSyncResolveCategoryID_* suite.</summary>
public sealed class PlaidSyncCategoryResolutionTests
{
    [Fact]
    public void PayrollNameOverridesPfcCategory()
    {
        Assert.Equal("income", PlaidSyncCategoryResolution.ResolveKey("ACME CORP PAYROLL", "TRANSFER_IN", "TRANSFER_IN_DEPOSIT"));
        Assert.Equal("income", PlaidSyncCategoryResolution.ResolveKey("payroll deposit", "", ""));
        Assert.Equal("income", PlaidSyncCategoryResolution.ResolveKey("Bi-Weekly Payroll", "GENERAL_MERCHANDISE", "GENERAL_MERCHANDISE_PET_SUPPLIES"));
    }

    [Fact]
    public void NonPayrollFallsBackToPfcMapping()
    {
        Assert.Equal("groceries", PlaidSyncCategoryResolution.ResolveKey("WHOLE FOODS", "FOOD_AND_DRINK", "FOOD_AND_DRINK_GROCERIES"));
        Assert.Equal("", PlaidSyncCategoryResolution.ResolveKey("UNKNOWN MERCHANT", "", ""));
    }

    [Fact]
    public void IncomePfcPrimaryResolvesWithoutPayrollInName()
    {
        // Must resolve to Income via Plaid's own classification, not just the name check.
        Assert.Equal("income", PlaidSyncCategoryResolution.ResolveKey("ACME CORP DIRECT DEP", "INCOME", "INCOME_WAGES"));
        Assert.Equal("income", PlaidSyncCategoryResolution.ResolveKey("IRS TREAS 310 TAX REF", "INCOME", "INCOME_TAX_REFUND"));
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
}
