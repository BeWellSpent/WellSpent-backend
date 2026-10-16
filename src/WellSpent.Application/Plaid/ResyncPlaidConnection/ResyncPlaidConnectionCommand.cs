using System.Globalization;
using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Users;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Plaid.ResyncPlaidConnection;

public sealed record ResyncPlaidConnectionCommand(Guid UserId, Guid ConnectionId) : IRequest<PlaidConnectionDto>;

/// <summary>Mirrors Go's PlaidService.ResyncConnection — the immediate sync this fires is the whole point, not just clearing the cursor.</summary>
public sealed class ResyncPlaidConnectionCommandHandler(
    PlaidAccessGuard plaidAccess,
    IPlaidItemRepository items,
    PlaidBackgroundSync backgroundSync,
    ILogger<ResyncPlaidConnectionCommandHandler> logger) : IRequestHandler<ResyncPlaidConnectionCommand, PlaidConnectionDto>
{
    public async Task<PlaidConnectionDto> Handle(ResyncPlaidConnectionCommand request, CancellationToken ct)
    {
        await plaidAccess.RequireUsAsync(request.UserId, ct);
        var item = await items.GetByIdAsync(request.ConnectionId, ct);
        if (item.UserId != request.UserId)
        {
            throw new ForbiddenException("only the member who linked this connection can resync it");
        }

        if (item.Status == "disconnected")
        {
            throw new AppValidationException("this connection is disconnected — reconnect it to import transactions again");
        }

        // A resync for a free-tier owner would clear the cursor and import nothing.
        var owner = await plaidAccess.RequireProOrLifetimeAsync(request.UserId, ct);

        if (PlaidConnectionRules.ResyncAvailableAt(item, DateTime.UtcNow) is { } nextAvailable)
        {
            throw new AppValidationException(
                $"this connection was already resynced recently — the next one is available after {nextAvailable.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)}");
        }

        var reset = await items.ResetCursorAsync(request.ConnectionId, ct);
        logger.LogInformation("plaid.resync_requested plaid_item_id={PlaidItemId} user_id={UserId}", reset.Id, request.UserId);

        // Detached: replaying a full history outlives the RPC that asked for it.
        backgroundSync.FireAndForgetSyncItem(reset.Id);

        var ownerName = UserDisplayRules.DisplayName(owner);
        return PlaidConnectionMapping.ToDto(reset, ownerName, true, true, PlaidConnectionRules.ResyncAvailableAt(reset, DateTime.UtcNow));
    }
}
