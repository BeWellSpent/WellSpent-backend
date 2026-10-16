using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WellSpent.Application;
using WellSpent.Application.Budgets;
using WellSpent.Application.Budgets.CreateBudgetPeriod;
using WellSpent.Application.Configuration;
using WellSpent.Application.Plaid;
using WellSpent.Domain.Abstractions;
using WellSpent.Infrastructure;
using WellSpent.Infrastructure.Configuration;

// Mirrors cmd/jobs/cycle-budgets/main.go. Unlike plaid-sync, this job defaults
// ENV to "dev" via AppConfig.Load() itself — no override needed here.
var config = AppConfig.Load();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
services.AddInfrastructure(config, applicationName: $"wellspent-cycle-budgets-{config.Env}");
services.AddApplication();
services.Configure<AuthOptions>(o =>
{
    o.FrontendUrl = config.FrontendUrl;
    o.ResendFromEmail = config.ResendFromEmail;
    o.CaptchaEnforcementEnabled = config.CaptchaEnforcementEnabled;
    o.EncryptionKey = config.EncryptionKey;
});

await using var provider = services.BuildServiceProvider();
var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("CycleBudgetsJob");

var profiles = provider.GetRequiredService<IBudgetProfileRepository>();
var plaidSync = provider.GetRequiredService<PlaidSyncEngine>();
var sender = provider.GetRequiredService<ISender>();

var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

List<Guid> profileIds;
try
{
    profileIds = await profiles.ListProfileIdsWithExpiredPeriodAsync(cutoff, CancellationToken.None);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "cycle-budgets.list_expired_profiles_failed");
    return 1;
}

logger.LogInformation("cycle-budgets.run_started profile_count={ProfileCount}", profileIds.Count);

var succeeded = 0;
var failed = 0;
foreach (var id in profileIds)
{
    WellSpent.Domain.Entities.BudgetProfile profile;
    try
    {
        profile = await profiles.GetByIdAsync(id, CancellationToken.None);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "cycle-budgets.load_profile_failed profile_id={ProfileId}", id);
        failed++;
        continue;
    }

    logger.LogInformation("cycle-budgets.cycle_started profile_id={ProfileId} name={Name} cycle={Cycle}", id, profile.Name, profile.Cycle);

    // A Plaid outage must not wedge a budget — cycling continues regardless of this outcome.
    ProfileSyncResult? syncResult = null;
    Exception? syncError = null;
    try
    {
        syncResult = await plaidSync.SyncProfileAsync(id, CancellationToken.None);
    }
    catch (Exception ex)
    {
        syncError = ex;
    }
    logger.LogInformation("cycle-budgets.pre_cycle_sync profile_id={ProfileId} summary={Summary}",
        id, CycleBudgetsJobReport.DescribePreSyncResult(syncResult, syncError));

    try
    {
        var period = await sender.Send(new CreateBudgetPeriodCommand(profile.UserId, id), CancellationToken.None);
        logger.LogInformation("cycle-budgets.cycle_ok profile_id={ProfileId} name={Name} new_period_start={Start} new_period_end={End}",
            id, profile.Name, period.StartDate, period.EndDate);
        succeeded++;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "cycle-budgets.create_period_failed profile_id={ProfileId} name={Name}", id, profile.Name);
        failed++;
    }
}

logger.LogInformation("cycle-budgets.run_finished succeeded={Succeeded} failed={Failed}", succeeded, failed);
if (failed > 0)
{
    logger.LogWarning("cycle-budgets.run_had_failures failed_count={FailedCount}", failed);
}

return 0;
