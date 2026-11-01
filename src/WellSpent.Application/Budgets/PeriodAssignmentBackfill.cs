using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets;

/// <summary>One-off, idempotent safety net: reassigns any transaction whose BudgetPeriodId doesn't actually cover its Date. Never touches a PeriodOverridden row.</summary>
public static class PeriodAssignmentBackfill
{
    public static async Task ApplyAsync(ITransactionRepository transactions, IBudgetProfileRepository profiles, ILogger logger, CancellationToken ct)
    {
        List<MismatchedPeriodAssignmentRow> mismatches;
        try
        {
            mismatches = await transactions.ListMismatchedPeriodAssignmentsAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "period_assignment_backfill.list_mismatches_failed");
            return;
        }

        logger.LogInformation("period_assignment_backfill.started mismatch_count={MismatchCount}", mismatches.Count);

        var reassigned = 0;
        var unresolved = new List<Guid>();
        foreach (var row in mismatches)
        {
            if (row.BudgetProfileId is not { } profileId)
            {
                // No BudgetPeriodId to resolve a profile from — nothing to search against.
                unresolved.Add(row.TransactionId);
                logger.LogWarning("period_assignment_backfill.unresolved transaction_id={TransactionId} reason=no_profile_id", row.TransactionId);
                continue;
            }
            if (row.Date is not { } date)
            {
                unresolved.Add(row.TransactionId);
                logger.LogWarning("period_assignment_backfill.unresolved transaction_id={TransactionId} reason=no_date", row.TransactionId);
                continue;
            }

            try
            {
                var correctPeriod = await profiles.GetPeriodByDateAsync(profileId, date, ct);
                if (correctPeriod is null)
                {
                    unresolved.Add(row.TransactionId);
                    logger.LogWarning("period_assignment_backfill.unresolved transaction_id={TransactionId} budget_profile_id={BudgetProfileId} date={Date} reason=no_covering_period",
                        row.TransactionId, profileId, date);
                    continue;
                }

                await transactions.ReassignPeriodAsync(row.TransactionId, correctPeriod.Id, ct);
                reassigned++;
                logger.LogInformation("period_assignment_backfill.reassigned transaction_id={TransactionId} budget_profile_id={BudgetProfileId} old_period_id={OldPeriodId} new_period_id={NewPeriodId}",
                    row.TransactionId, profileId, row.CurrentBudgetPeriodId, correctPeriod.Id);
            }
            catch (Exception ex)
            {
                unresolved.Add(row.TransactionId);
                logger.LogError(ex, "period_assignment_backfill.reassign_failed transaction_id={TransactionId} budget_profile_id={BudgetProfileId}", row.TransactionId, profileId);
            }
        }

        logger.LogInformation("period_assignment_backfill.finished checked={Checked} reassigned={Reassigned} unresolved_count={UnresolvedCount} unresolved_ids={UnresolvedIds}",
            mismatches.Count, reassigned, unresolved.Count, string.Join(",", unresolved));
    }
}
