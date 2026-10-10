using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Plaid;

/// <summary>
/// Fires an immediate sync for one item, detached from the calling request —
/// mirrors Go's <c>go func() { s.SyncItem(context.Background(), item) }()</c>
/// in ExchangePublicTokenCommand and ResyncPlaidConnectionCommand. Needs its
/// own DI scope: the request's scope (and its DbContext) is disposed once
/// the response is sent, well before this work finishes, so the engine and
/// its repositories must be resolved fresh inside a new scope rather than
/// captured from the caller's.
/// </summary>
public sealed class PlaidBackgroundSync(IServiceScopeFactory scopeFactory, ILogger<PlaidBackgroundSync> logger)
{
    public void FireAndForgetSyncItem(Guid plaidItemId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var items = scope.ServiceProvider.GetRequiredService<IPlaidItemRepository>();
                var engine = scope.ServiceProvider.GetRequiredService<PlaidSyncEngine>();
                var item = await items.GetByIdAsync(plaidItemId, CancellationToken.None);
                await engine.SyncItemAsync(item, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "plaid.background_sync_failed plaid_item_id={PlaidItemId}", plaidItemId);
            }
        });
    }
}
