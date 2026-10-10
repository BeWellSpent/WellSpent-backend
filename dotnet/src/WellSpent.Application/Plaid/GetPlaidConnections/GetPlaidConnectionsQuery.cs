using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.Users;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Plaid.GetPlaidConnections;

public sealed record GetPlaidConnectionsQuery(Guid UserId, Guid? BudgetProfileId) : IRequest<GetPlaidConnectionsResult>;

public sealed record GetPlaidConnectionsResult(List<PlaidConnectionDto> Connections, List<BudgetSyncWarningDto> Warnings);

/// <summary>Mirrors Go's PlaidService.GetConnections + ListSyncWarnings, combined the way the handler already does on the Go side.</summary>
public sealed class GetPlaidConnectionsQueryHandler(
    PlaidAccessGuard plaidAccess,
    BudgetAccessGuard budgetAccess,
    IPlaidItemRepository items,
    IUserRepository users,
    ILogger<GetPlaidConnectionsQueryHandler> logger) : IRequestHandler<GetPlaidConnectionsQuery, GetPlaidConnectionsResult>
{
    public async Task<GetPlaidConnectionsResult> Handle(GetPlaidConnectionsQuery request, CancellationToken ct)
    {
        await plaidAccess.RequireUsAsync(request.UserId, ct);
        var now = DateTime.UtcNow;

        List<PlaidConnectionDto> connections;
        if (request.BudgetProfileId is { } profileId)
        {
            await budgetAccess.EnsureMemberForbiddenAsync(profileId, request.UserId, ct);
            var rows = await items.ListActiveWithOwnerByBudgetProfileAsync(profileId, ct);
            connections = rows.Select(row =>
            {
                var isOwner = row.Item.UserId == request.UserId;
                // Only the owner can act on a resync.
                var resyncAvailableAt = isOwner ? PlaidConnectionRules.ResyncAvailableAt(row.Item, now) : null;
                return PlaidConnectionMapping.ToDto(row.Item, row.OwnerName, isOwner, row.OwnerPlan != "free", resyncAvailableAt);
            }).ToList();
        }
        else
        {
            var itemList = await items.ListByUserIdAsync(request.UserId, ct);
            var caller = await users.GetByIdAsync(request.UserId, ct);
            var ownerName = UserDisplayRules.DisplayName(caller);
            connections = itemList
                .Select(item => PlaidConnectionMapping.ToDto(
                    item, ownerName, true, caller.Plan != "free", PlaidConnectionRules.ResyncAvailableAt(item, now)))
                .ToList();
        }

        // Best-effort — a broken warning shouldn't break the whole screen.
        List<BudgetSyncWarningDto> warnings;
        try
        {
            var rows = await items.ListUnsyncableForUserAsync(request.UserId, ct);
            warnings = rows
                .Select(r => new BudgetSyncWarningDto(r.BudgetProfileId, r.BudgetName, r.MemberName, r.ConnectionCount, r.MemberUserId == request.UserId))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.list_sync_warnings_failed user_id={UserId}", request.UserId);
            warnings = [];
        }

        return new GetPlaidConnectionsResult(connections, warnings);
    }
}
