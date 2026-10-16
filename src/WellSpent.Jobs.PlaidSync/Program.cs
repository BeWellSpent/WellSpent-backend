using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Application.Plaid;
using WellSpent.Infrastructure;
using WellSpent.Infrastructure.Configuration;

// Mirrors cmd/jobs/plaid-sync/main.go. Cloud Run Job sets PLAID_ENV but not ENV, so default to prod.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENV")))
{
    Environment.SetEnvironmentVariable("ENV", "prod");
}

var config = AppConfig.Load();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
services.AddInfrastructure(config, applicationName: $"wellspent-plaid-sync-{config.Env}");
services.AddApplication();
services.Configure<AuthOptions>(o =>
{
    o.FrontendUrl = config.FrontendUrl;
    o.ResendFromEmail = config.ResendFromEmail;
    o.CaptchaEnforcementEnabled = config.CaptchaEnforcementEnabled;
    o.EncryptionKey = config.EncryptionKey;
});

await using var provider = services.BuildServiceProvider();
var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("PlaidSyncJob");

var engine = provider.GetRequiredService<PlaidSyncEngine>();
List<ProfileSyncResult> profiles;
try
{
    profiles = await engine.SyncAllAsync(CancellationToken.None);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "plaid-sync.list_active_items_failed");
    return 1;
}

var (failures, skipped) = PlaidSyncJobReport.Report(profiles, logger);

if (failures.Count == 0 && skipped.Count == 0)
{
    return 0;
}

var alertEmail = Environment.GetEnvironmentVariable("PLAID_SYNC_ALERT_EMAIL");

if (string.IsNullOrEmpty(config.ResendApiKey) || string.IsNullOrEmpty(alertEmail))
{
    logger.LogWarning(
        "plaid-sync.alert_email_not_sent failure_count={FailureCount} skipped_count={SkippedCount} reason=RESEND_API_KEY_or_PLAID_SYNC_ALERT_EMAIL_unset",
        failures.Count, skipped.Count);
    return 0;
}

try
{
    var emailSender = provider.GetRequiredService<IEmailSender>();
    var (subject, body) = PlaidSyncJobReport.BuildFailureEmail(failures, skipped);
    await emailSender.SendAsync(alertEmail, subject, body, CancellationToken.None);
    logger.LogInformation("plaid-sync.alert_email_sent to={AlertEmail} failure_count={FailureCount} skipped_count={SkippedCount}",
        alertEmail, failures.Count, skipped.Count);
}
catch (Exception ex)
{
    logger.LogError(ex, "plaid-sync.alert_email_failed to={AlertEmail}", alertEmail);
}

return 0;
