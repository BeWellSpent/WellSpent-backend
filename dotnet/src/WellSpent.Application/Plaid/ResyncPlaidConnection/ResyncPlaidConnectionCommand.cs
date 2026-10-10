using System.Globalization;
using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Users;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Plaid.ResyncPlaidConnection;

public sealed record ResyncPlaidConnectionCommand(Guid UserId, Guid ConnectionId) : IRequest<PlaidConnectionDto>;

/// <summary>
/// Mirrors Go's PlaidService.ResyncConnection. Clearing the cursor alone
/// would only make the connection eligible for the scheduled job (Mon/Wed/
/// Fri) — the immediate sync this triggers (HOOK, B6 batch 3) is the whole
/// point, since a button that did just that would appear to do nothing for
/// up to three days.
/// </summary>
public sealed class ResyncPlaidConnectionCommandHandler(
    PlaidAccessGuard plaidAccess,
    IPlaidItemRepository items,
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

        // Pointless rather than merely unentitled: the sync job skips free-tier
        // owners on every run, so a resync would clear the cursor and import
        // nothing, losing the user's place for no benefit.
        var owner = await plaidAccess.RequireProOrLifetimeAsync(request.UserId, ct);

        if (PlaidConnectionRules.ResyncAvailableAt(item, DateTime.UtcNow) is { } nextAvailable)
        {
            throw new AppValidationException(
                $"this connection was already resynced recently — the next one is available after {nextAvailable.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)}");
        }

        var reset = await items.ResetCursorAsync(request.ConnectionId, ct);
        logger.LogInformation("plaid.resync_requested plaid_item_id={PlaidItemId} user_id={UserId}", reset.Id, request.UserId);

        // HOOK: trigger an immediate background sync for this item, detached
        // from the request (B6 batch 3 — SyncItem doesn't exist yet).

        var ownerName = UserDisplayRules.DisplayName(owner);
        return PlaidConnectionMapping.ToDto(reset, ownerName, true, true, PlaidConnectionRules.ResyncAvailableAt(reset, DateTime.UtcNow));
    }
}
