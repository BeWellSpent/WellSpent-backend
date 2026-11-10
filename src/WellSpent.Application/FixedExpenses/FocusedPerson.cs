using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses;

/// <summary>The caller's own budget person when focused view is on; null means everyone.</summary>
internal static class FocusedPerson
{
    public static async Task<int?> ResolveAsync(IBudgetProfileRepository profiles, Guid budgetProfileId, Guid userId, bool focusedView, CancellationToken ct)
    {
        if (!focusedView) return null;
        try
        {
            return (await profiles.GetPersonByUserIdAsync(budgetProfileId, userId, ct)).Id;
        }
        catch (NotFoundException)
        {
            throw new ForbiddenException("access denied");
        }
    }
}
