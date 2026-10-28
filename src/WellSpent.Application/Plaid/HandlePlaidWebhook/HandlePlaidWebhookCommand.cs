using Going.Plaid;
using Going.Plaid.Webhook;
using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Plaid.HandlePlaidWebhook;

/// <summary>Payload is already signature-verified and deserialized by the endpoint — this handler only routes it.</summary>
public sealed record HandlePlaidWebhookCommand(WebhookBase Payload) : IRequest;

/// <summary>
/// Dispatches a verified Plaid webhook to the matching action. Always
/// "succeeds" — an unrecognized webhook or one naming an item we don't know
/// is logged and no-op'd, never thrown; the endpoint always returns 200 to
/// Plaid regardless (only an unverifiable signature, checked before this
/// handler runs, should produce a non-200 — see Plaid's own retry guidance).
/// </summary>
public sealed class HandlePlaidWebhookCommandHandler(
    IPlaidItemRepository items,
    PlaidBackgroundSync backgroundSync,
    ILogger<HandlePlaidWebhookCommandHandler> logger) : IRequestHandler<HandlePlaidWebhookCommand>
{
    public async Task Handle(HandlePlaidWebhookCommand request, CancellationToken ct)
    {
        switch (request.Payload)
        {
            case SyncUpdatesAvailableWebhook sync:
                await HandleSyncUpdatesAvailableAsync(sync.ItemId, ct);
                break;

            case ItemErrorWebhook error:
                await HandleItemErrorAsync(error.ItemId, ct);
                break;

            default:
                logger.LogInformation(
                    "plaid.webhook.ignored webhook_type={WebhookType} webhook_code={WebhookCode}",
                    request.Payload.WebhookType, request.Payload.WebhookCode);
                break;
        }
    }

    private async Task HandleSyncUpdatesAvailableAsync(string itemId, CancellationToken ct)
    {
        var item = await items.GetByItemIdAsync(itemId, ct);
        if (item is null)
        {
            logger.LogWarning("plaid.webhook.unknown_item item_id={ItemId} webhook_code=SYNC_UPDATES_AVAILABLE", itemId);
            return;
        }

        // Unconditional now — the old "only if never synced" gate existed solely to avoid double-syncing against the Mon/Wed/Fri cron, which no longer exists.
        logger.LogInformation("plaid.webhook.sync_requested plaid_item_id={PlaidItemId} item_id={ItemId}", item.Id, itemId);
        backgroundSync.FireAndForgetSyncItem(item.Id);
    }

    private async Task HandleItemErrorAsync(string itemId, CancellationToken ct)
    {
        var item = await items.GetByItemIdAsync(itemId, ct);
        if (item is null)
        {
            logger.LogWarning("plaid.webhook.unknown_item item_id={ItemId} webhook_code=ERROR", itemId);
            return;
        }

        logger.LogWarning("plaid.webhook.item_error plaid_item_id={PlaidItemId} item_id={ItemId}", item.Id, itemId);
        await items.UpdateStatusAsync(item.Id, "error", ct);
    }
}
