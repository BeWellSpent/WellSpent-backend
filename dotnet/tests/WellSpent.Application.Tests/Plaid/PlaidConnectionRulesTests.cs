using WellSpent.Application.Plaid;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/service/plaid_service_test.go's TestResyncAvailableAt_* suite.</summary>
public sealed class PlaidConnectionRulesTests
{
    [Fact]
    public void ResyncAvailableAt_NeverResynced_IsAllowedNow()
    {
        Assert.Null(PlaidConnectionRules.ResyncAvailableAt(new PlaidItem { AccessToken = "x", ItemId = "x" }, DateTime.UtcNow));
    }

    [Fact]
    public void ResyncAvailableAt_ReportsExactUnlockTime()
    {
        var last = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
        var item = new PlaidItem { AccessToken = "x", ItemId = "x", LastManualResyncAt = last };

        var next = PlaidConnectionRules.ResyncAvailableAt(item, last.AddHours(1));

        Assert.Equal(last.AddHours(24), next);
    }

    [Fact]
    public void ResyncAvailableAt_AtExactCooldownBoundary_IsAllowedNow()
    {
        var last = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
        var item = new PlaidItem { AccessToken = "x", ItemId = "x", LastManualResyncAt = last };

        Assert.Null(PlaidConnectionRules.ResyncAvailableAt(item, last.AddHours(24)));
    }
}
