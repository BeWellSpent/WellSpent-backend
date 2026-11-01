using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WellSpent.Application;
using WellSpent.Application.Budgets;
using WellSpent.Domain.Abstractions;
using WellSpent.Infrastructure;
using WellSpent.Infrastructure.Configuration;

// Manual, run-once-per-environment safety net (issue #99) — no Cloud Scheduler
// trigger. Run with: dotnet WellSpent.Jobs.BackfillPeriodAssignments.dll
var config = AppConfig.Load();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning));
services.AddInfrastructure(config, applicationName: $"wellspent-backfill-period-assignments-{config.Env}");
services.AddApplication();

await using var provider = services.BuildServiceProvider();
var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("PeriodAssignmentBackfillJob");

var transactions = provider.GetRequiredService<ITransactionRepository>();
var profiles = provider.GetRequiredService<IBudgetProfileRepository>();

await PeriodAssignmentBackfill.ApplyAsync(transactions, profiles, logger, CancellationToken.None);

return 0;
