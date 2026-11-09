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
public sealed class PlaidItemSync(IServiceScopeFactory scopeFactory, ILogger<PlaidItemSync> logger)
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task>> inFlight = new();

    public Task SyncItemAsync(Guid plaidItemId) =>
        inFlight.GetOrAdd(plaidItemId, id => new Lazy<Task>(() => RunAsync(id))).Value;

    private async Task RunAsync(Guid plaidItemId)
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
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.item_sync_failed plaid_item_id={PlaidItemId}", plaidItemId);
        }
        finally
        {
            inFlight.TryRemove(plaidItemId, out _);
        }
    }
}
