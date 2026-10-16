using WellSpent.Domain.Entities;

namespace WellSpent.Application.Plaid;

public sealed record PlaidConnectionDto(
    Guid Id, string InstitutionId, string InstitutionName, string Status,
    DateTime? LastSyncedAt, Guid BudgetProfileId, string OwnerName, bool IsOwner,
    bool SyncEnabled, DateTime? ResyncAvailableAt);

/// <summary>Aggregate, not the connections themselves — other members shouldn't see which institutions someone banks with.</summary>
public sealed record BudgetSyncWarningDto(Guid BudgetProfileId, string BudgetName, string MemberName, int ConnectionCount, bool IsCurrentUser);

/// <summary>Mirrors internal/service/plaid_service.go's resyncAvailableAt exactly.</summary>
public static class PlaidConnectionRules
{
    private static readonly TimeSpan ManualResyncCooldown = TimeSpan.FromHours(24);

    /// <summary>Returns when the next manual resync is allowed, or null if one is allowed right now.</summary>
    public static DateTime? ResyncAvailableAt(PlaidItem item, DateTime now)
    {
        if (item.LastManualResyncAt is not { } last)
        {
            return null;
        }

        var next = last.Add(ManualResyncCooldown);
        return now < next ? next : null;
    }
}

public static class PlaidConnectionMapping
{
    /// <summary>Bare item, no owner context — mirrors Go's toProtoPlaidConnection; both clients refetch the list instead of rendering this.</summary>
    public static PlaidConnectionDto ToDto(PlaidItem item) =>
        new(item.Id, item.InstitutionId ?? "", item.InstitutionName ?? "", item.Status,
            item.LastSyncedAt, item.BudgetProfileId, "", false, false, null);

    /// <summary>Adds the per-caller fields that only make sense once a budget's connections from several members appear in one list.</summary>
    public static PlaidConnectionDto ToDto(PlaidItem item, string ownerName, bool isOwner, bool syncEnabled, DateTime? resyncAvailableAt) =>
        new(item.Id, item.InstitutionId ?? "", item.InstitutionName ?? "", item.Status,
            item.LastSyncedAt, item.BudgetProfileId, ownerName, isOwner, syncEnabled, resyncAvailableAt);
}
