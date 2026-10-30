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

// --region=emea|americas narrows this run to one Cloud Scheduler invocation's
// slice of profiles, so each region's cutoff fires only once it's actually
// past local midnight there — see ProfileTimeZone. Absent (e.g. the manual
// run-csharp-job.yml trigger), every profile is evaluated.
var region = args.FirstOrDefault(a => a.StartsWith("--region=", StringComparison.Ordinal))?["--region=".Length..];

var utcNow = DateTime.UtcNow;
// Loose DB-level filter only — the precise per-profile local-midnight check happens below.
var cutoff = DateOnly.FromDateTime(utcNow);

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

logger.LogInformation("cycle-budgets.run_started region={Region} profile_count={ProfileCount}", region ?? "all", profileIds.Count);

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

    if (!ProfileTimeZone.MatchesRegion(profile.CountryCode, region))
    {
        continue; // a different scheduled invocation is responsible for this profile
    }

    WellSpent.Domain.Entities.BudgetPeriod latestPeriod;
    try
    {
        latestPeriod = await profiles.GetLatestPeriodAsync(id, CancellationToken.None);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "cycle-budgets.load_latest_period_failed profile_id={ProfileId}", id);
        failed++;
        continue;
    }
    if (!ProfileTimeZone.IsPastLocalMidnightAfter(latestPeriod.EndDate, profile.CountryCode, utcNow))
    {
        logger.LogInformation("cycle-budgets.not_yet_local_midnight profile_id={ProfileId} country={Country} end_date={EndDate}", id, profile.CountryCode, latestPeriod.EndDate);
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
