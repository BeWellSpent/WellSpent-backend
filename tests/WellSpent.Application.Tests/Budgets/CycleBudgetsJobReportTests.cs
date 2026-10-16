using WellSpent.Application.Budgets;
using WellSpent.Application.Plaid;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

/// <summary>Mirrors Go's cmd/jobs/cycle-budgets/main.go describePreSyncResult tests.</summary>
public sealed class CycleBudgetsJobReportTests
{
    [Fact]
    public void SyncFailed_ReportsErrorAndStillCycles()
    {
        var summary = CycleBudgetsJobReport.DescribePreSyncResult(null, new InvalidOperationException("plaid down"));
        Assert.Equal("sync failed: plaid down — cycling anyway", summary);
    }

    [Fact]
    public void NoItems_ReportsNoConnections()
    {
        var summary = CycleBudgetsJobReport.DescribePreSyncResult(new ProfileSyncResult { ProfileId = Guid.NewGuid() }, null);
        Assert.Equal("no Plaid connections", summary);
    }

    [Fact]
    public void NullResult_ReportsNoConnections()
    {
        var summary = CycleBudgetsJobReport.DescribePreSyncResult(null, null);
        Assert.Equal("no Plaid connections", summary);
    }

    [Fact]
    public void AllSucceeded_SummarizesImportedAndRepointed()
    {
        var result = new ProfileSyncResult
        {
            ProfileId = Guid.NewGuid(),
            Items = [new ItemSyncResult { ItemId = Guid.NewGuid(), Imported = 3, Repointed = 1 }, new ItemSyncResult { ItemId = Guid.NewGuid(), Imported = 2 }],
        };

        var summary = CycleBudgetsJobReport.DescribePreSyncResult(result, null);

        Assert.Equal("synced 2 connection(s), 5 imported, 1 repointed", summary);
    }

    [Fact]
    public void SomeItemsFailed_StillReportsAndCyclesAnyway()
    {
        var result = new ProfileSyncResult
        {
            ProfileId = Guid.NewGuid(),
            Items =
            [
                new ItemSyncResult { ItemId = Guid.NewGuid(), Imported = 3 },
                new ItemSyncResult { ItemId = Guid.NewGuid(), Error = new InvalidOperationException("boom") },
            ],
        };

        var summary = CycleBudgetsJobReport.DescribePreSyncResult(result, null);

        Assert.Equal("synced 2 connection(s), 3 imported, 0 repointed, 1 failed — cycling anyway", summary);
    }
}
