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
    PlaidItemSync itemSync,
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

        // Awaited so the instance keeps its CPU until the sync finishes; a Plaid retry of a slow reply joins the run already going.
        logger.LogInformation("plaid.webhook.sync_requested plaid_item_id={PlaidItemId} item_id={ItemId}", item.Id, itemId);
        await itemSync.SyncItemAsync(item.Id);
        logger.LogInformation("plaid.webhook.sync_finished plaid_item_id={PlaidItemId} item_id={ItemId}", item.Id, itemId);
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
