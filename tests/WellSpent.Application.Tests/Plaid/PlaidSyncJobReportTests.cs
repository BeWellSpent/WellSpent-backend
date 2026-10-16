using Microsoft.Extensions.Logging.Abstractions;
using WellSpent.Application.Plaid;

namespace WellSpent.Application.Tests.Plaid;

public sealed class PlaidSyncJobReportTests
{
    [Fact]
    public void Report_SeparatesFailuresFromSkips()
    {
        var profileId = Guid.NewGuid();
        var profiles = new List<ProfileSyncResult>
        {
            new()
            {
                ProfileId = profileId,
                Items =
                [
                    new ItemSyncResult { ItemId = Guid.NewGuid(), InstitutionName = "Chase", Error = new Exception("boom") },
                    new ItemSyncResult { ItemId = Guid.NewGuid(), InstitutionName = "Ally", SkippedUnentitled = true },
                    new ItemSyncResult { ItemId = Guid.NewGuid(), InstitutionName = "Amex" },
                ],
            },
        };

        var (failures, skipped) = PlaidSyncJobReport.Report(profiles, NullLogger.Instance);

        Assert.Single(failures);
        Assert.Equal("Chase", failures[0].Institution);
        Assert.Single(skipped);
        Assert.Equal("Ally", skipped[0].Institution);
    }

    [Fact]
    public void DescribeAccounts_NoActivity_ReportsModifiedAndRemoved()
    {
        var result = new ItemSyncResult { ItemId = Guid.NewGuid(), Modified = 2, Removed = 1 };

        Assert.Equal("no new transactions (2 modified, 1 removed)", PlaidSyncJobReport.DescribeAccounts(result));
    }

    [Fact]
    public void DescribeAccounts_WithActivity_ListsAccountsBusiestFirst()
    {
        var result = new ItemSyncResult
        {
            ItemId = Guid.NewGuid(),
            ByAccount = [new AccountImport("Chase Checking", 2), new AccountImport("Amex", 1)],
            AutoConfirmed = 1,
            Queued = 2,
        };

        Assert.Equal("imported Chase Checking: 2, Amex: 1 (1 auto-confirmed, 2 queued for review)", PlaidSyncJobReport.DescribeAccounts(result));
    }

    [Fact]
    public void BuildFailureEmail_EscapesHtmlAndIncludesBothSections()
    {
        var failures = new List<PlaidSyncFailure> { new("<p1>", "Chase", "item-1", new Exception("boom <script>")) };
        var skipped = new List<PlaidSyncSkip> { new("p2", "Ally", "item-2") };

        var (subject, body) = PlaidSyncJobReport.BuildFailureEmail(failures, skipped);

        Assert.Equal("WellSpent Plaid sync: 1 failed, 1 skipped", subject);
        Assert.Contains("&lt;p1&gt;", body);
        Assert.Contains("boom &lt;script&gt;", body);
        Assert.Contains("Ally", body);
    }
}
