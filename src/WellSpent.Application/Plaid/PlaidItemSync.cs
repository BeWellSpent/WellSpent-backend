using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Plaid;

/// <summary>
/// Syncs one connection to completion in its own DI scope. Callers await it from inside their
/// request: on Cloud Run a detached task loses its CPU and its instance once no request is in
/// flight, which killed syncs mid-run. Overlapping calls for one item (a Plaid webhook retry)
/// share a single run. Never throws — a failed sync is logged, and the cursor stays put.
/// </summary>
public class PlaidItemSync(IServiceScopeFactory scopeFactory, ILogger<PlaidItemSync> logger)
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<bool>>> inFlight = new();

    /// <summary>True when the run completed cleanly. Webhook and link callers ignore it; "Sync now" turns false into an error.</summary>
    public virtual Task<bool> SyncItemAsync(Guid plaidItemId) =>
        inFlight.GetOrAdd(plaidItemId, id => new Lazy<Task<bool>>(() => RunAsync(id))).Value;

    private async Task<bool> RunAsync(Guid plaidItemId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var items = scope.ServiceProvider.GetRequiredService<IPlaidItemRepository>();
            var engine = scope.ServiceProvider.GetRequiredService<PlaidSyncEngine>();
            var item = await items.GetByIdAsync(plaidItemId, CancellationToken.None);
            // None, not the request's token: Plaid timing out its webhook must not abort the run.
            var result = await engine.SyncItemAsync(item, CancellationToken.None);
            if (result.Error is not null)
            {
                logger.LogError(result.Error, "plaid.item_sync_failed plaid_item_id={PlaidItemId}", plaidItemId);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.item_sync_failed plaid_item_id={PlaidItemId}", plaidItemId);
            return false;
        }
        finally
        {
            inFlight.TryRemove(plaidItemId, out _);
        }
    }
}
