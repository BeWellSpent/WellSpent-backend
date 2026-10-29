using WellSpent.Domain.Entities;

namespace WellSpent.Application.FixedExpenses;

/// <summary>Picks which spawned occurrence of a fixed expense template a real payment belongs to.</summary>
public static class FixedExpenseOccurrenceMatching
{
    /// <summary>
    /// The candidate closest to targetDate — a biweekly/weekly template can
    /// spawn several occurrences in one period, so picking just "the latest"
    /// cross-matches a real payment to the wrong one once there's more than one.
    /// Falls back to latest when there's no target date to compare against.
    /// </summary>
    public static Transaction? ClosestTo(List<Transaction> candidates, DateOnly? targetDate)
    {
        if (candidates.Count == 0) return null;
        if (targetDate is not { } target) return candidates.OrderByDescending(t => t.Date).First();
        return candidates.MinBy(t => Math.Abs((t.Date ?? target).DayNumber - target.DayNumber));
    }
}
