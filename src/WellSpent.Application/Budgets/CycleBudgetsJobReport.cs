using WellSpent.Application.Plaid;

namespace WellSpent.Application.Budgets;

/// <summary>Mirrors cmd/jobs/cycle-budgets/main.go's describePreSyncResult — summarizes the pre-cycle Plaid sync for the job's log line.</summary>
public static class CycleBudgetsJobReport
{
    public static string DescribePreSyncResult(ProfileSyncResult? result, Exception? error)
    {
        if (error is not null) return $"sync failed: {error.Message} — cycling anyway";
        if (result is null || result.Items.Count == 0) return "no Plaid connections";

        var imported = result.Items.Sum(i => i.Imported);
        var repointed = result.Items.Sum(i => i.Repointed);
        var failed = result.Items.Count(i => i.Error is not null);

        return failed > 0
            ? $"synced {result.Items.Count} connection(s), {imported} imported, {repointed} repointed, {failed} failed — cycling anyway"
            : $"synced {result.Items.Count} connection(s), {imported} imported, {repointed} repointed";
    }
}
