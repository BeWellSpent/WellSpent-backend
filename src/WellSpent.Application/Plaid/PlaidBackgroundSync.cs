using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Plaid;

/// <summary>Fires a detached sync (mirrors Go's goroutine) in its own DI scope, since the request's scope is disposed first.</summary>
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
