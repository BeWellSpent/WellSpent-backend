using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Plaid.DisconnectPlaid;

public sealed record DisconnectPlaidCommand(Guid UserId, Guid ConnectionId) : IRequest<Unit>;

/// <summary>Mirrors Go's PlaidService.Disconnect. Removing the item at Plaid is best-effort — a failure there is logged (the user keeps being billed for a connection they believe they removed) but never blocks the local disconnect.</summary>
public sealed class DisconnectPlaidCommandHandler(
    PlaidAccessGuard plaidAccess,
    IPlaidItemRepository items,
    IPlaidClient plaid,
    ICryptoService crypto,
    IOptions<AuthOptions> options,
    ILogger<DisconnectPlaidCommandHandler> logger) : IRequestHandler<DisconnectPlaidCommand, Unit>
{
    public async Task<Unit> Handle(DisconnectPlaidCommand request, CancellationToken ct)
    {
        await plaidAccess.RequireUsAsync(request.UserId, ct);
        var item = await items.GetByIdAsync(request.ConnectionId, ct);
        if (item.UserId != request.UserId)
        {
            throw new ForbiddenException("access denied");
        }

        try
        {
            var decrypted = crypto.Decrypt(item.AccessToken, options.Value.EncryptionKey);
            try
            {
                await plaid.RemoveItemAsync(decrypted, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "plaid.remove_item_at_plaid_failed plaid_item_id={PlaidItemId}", item.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.decrypt_access_token_failed_during_disconnect plaid_item_id={PlaidItemId}", item.Id);
        }

        await items.UpdateStatusAsync(request.ConnectionId, "disconnected", ct);
        return Unit.Value;
    }
}
