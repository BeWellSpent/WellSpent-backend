using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Users;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Plaid.SyncPlaidConnection;

public sealed record SyncPlaidConnectionCommand(Guid UserId, Guid ConnectionId) : IRequest<PlaidConnectionDto>;

/// <summary>"Sync now": the owner re-runs a sync from the connection's current cursor. Unlike resync it clears nothing and has no cooldown.</summary>
public sealed class SyncPlaidConnectionCommandHandler(
    PlaidAccessGuard plaidAccess,
    IPlaidItemRepository items,
    PlaidItemSync itemSync,
    ILogger<SyncPlaidConnectionCommandHandler> logger) : IRequestHandler<SyncPlaidConnectionCommand, PlaidConnectionDto>
{
    public async Task<PlaidConnectionDto> Handle(SyncPlaidConnectionCommand request, CancellationToken ct)
    {
        await plaidAccess.RequireUsAsync(request.UserId, ct);
        var item = await items.GetByIdAsync(request.ConnectionId, ct);
        if (item.UserId != request.UserId)
        {
            throw new ForbiddenException("only the member who linked this connection can sync it");
        }

        if (item.Status == "disconnected")
        {
            throw new AppValidationException("this connection is disconnected — reconnect it to import transactions again");
        }

        var owner = await plaidAccess.RequireProOrLifetimeAsync(request.UserId, ct);

        logger.LogInformation("plaid.sync_now_requested plaid_item_id={PlaidItemId} user_id={UserId}", item.Id, request.UserId);
        if (!await itemSync.SyncItemAsync(item.Id))
        {
            // The failure itself is already logged with its cause by PlaidItemSync.
            throw new InvalidOperationException("the sync did not finish — try again in a moment");
        }

        var synced = await items.GetByIdAsync(item.Id, ct);
        return PlaidConnectionMapping.ToDto(synced, UserDisplayRules.DisplayName(owner), true, true, PlaidConnectionRules.ResyncAvailableAt(synced, DateTime.UtcNow));
    }
}
