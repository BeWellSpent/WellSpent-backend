using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WellSpent.Application.Plaid;

public sealed record PlaidSyncFailure(string Profile, string Institution, string ItemId, Exception Error);

/// <summary>A connection skipped because its owner isn't entitled — reported, not silently dropped.</summary>
public sealed record PlaidSyncSkip(string Profile, string Institution, string ItemId);

/// <summary>Mirrors cmd/jobs/plaid-sync/main.go's reportRun/describeAccounts/buildFailureEmail.</summary>
public static class PlaidSyncJobReport
{
    public static (List<PlaidSyncFailure> Failures, List<PlaidSyncSkip> Skipped) Report(List<ProfileSyncResult> profiles, ILogger logger)
    {
        var failures = new List<PlaidSyncFailure>();
        var skipped = new List<PlaidSyncSkip>();

        logger.LogInformation("plaid-sync.run_started profile_count={ProfileCount}", profiles.Count);
        foreach (var profile in profiles)
        {
            var profileId = profile.ProfileId.ToString();
            foreach (var item in profile.Items)
            {
                var institution = string.IsNullOrEmpty(item.InstitutionName) ? "(unknown institution)" : item.InstitutionName;
                var itemId = item.ItemId.ToString();

                if (item.Error is not null)
                {
                    logger.LogError(item.Error, "plaid-sync.item_failed profile_id={ProfileId} institution={Institution} item_id={ItemId}",
                        profileId, institution, itemId);
                    failures.Add(new PlaidSyncFailure(profileId, institution, itemId, item.Error));
                }
                else if (item.SkippedUnentitled)
                {
                    logger.LogWarning("plaid-sync.item_skipped_unentitled profile_id={ProfileId} institution={Institution} item_id={ItemId}",
                        profileId, institution, itemId);
                    skipped.Add(new PlaidSyncSkip(profileId, institution, itemId));
                }
                else
                {
                    logger.LogInformation("plaid-sync.item_ok profile_id={ProfileId} institution={Institution} item_id={ItemId} summary={Summary}",
                        profileId, institution, itemId, DescribeAccounts(item));
                }
            }
        }

        return (failures, skipped);
    }

    public static string DescribeAccounts(ItemSyncResult item)
    {
        if (item.ByAccount.Count == 0)
        {
            return $"no new transactions ({item.Modified} modified, {item.Removed} removed)";
        }

        var parts = item.ByAccount.Select(a => $"{a.Account}: {a.Count}");
        return $"imported {string.Join(", ", parts)} ({item.AutoConfirmed} auto-confirmed, {item.Queued} queued for review)";
    }

    public static (string Subject, string Body) BuildFailureEmail(List<PlaidSyncFailure> failures, List<PlaidSyncSkip> skipped)
    {
        var subject = (failures.Count, skipped.Count) switch
        {
            ( > 0, > 0) => $"WellSpent Plaid sync: {failures.Count} failed, {skipped.Count} skipped",
            ( > 0, _) => $"WellSpent Plaid sync: {failures.Count} item(s) failed",
            _ => $"WellSpent Plaid sync: {skipped.Count} connection(s) skipped",
        };

        var body = new StringBuilder();
        if (failures.Count > 0)
        {
            body.Append($"<p>{failures.Count} Plaid connection(s) failed during this run:</p><ul>");
            foreach (var f in failures)
            {
                body.Append(
                    $"<li>budget <code>{WebUtility.HtmlEncode(f.Profile)}</code> — {WebUtility.HtmlEncode(f.Institution)} (<code>{WebUtility.HtmlEncode(f.ItemId)}</code>): {WebUtility.HtmlEncode(f.Error.Message)}</li>");
            }

            body.Append("</ul>");
        }

        if (skipped.Count > 0)
        {
            body.Append($"<p>{skipped.Count} connection(s) skipped — not on a paid plan, so they will never sync:</p><ul>");
            foreach (var s in skipped)
            {
                body.Append($"<li>budget <code>{WebUtility.HtmlEncode(s.Profile)}</code> — {WebUtility.HtmlEncode(s.Institution)} (<code>{WebUtility.HtmlEncode(s.ItemId)}</code>)</li>");
            }

            body.Append("</ul>");
        }

        return (subject, body.ToString());
    }
}
