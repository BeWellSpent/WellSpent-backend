using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>
/// Translates EF Core's DbUpdateConcurrencyException (thrown by SaveChangesAsync
/// against the xmin concurrency token on Transaction/FixedExpense) into the
/// domain-level ConflictException, so repositories never leak an EF Core
/// exception type past Infrastructure. A free function rather than a method on
/// each repository so the translation itself is unit-testable without a real
/// DbContext — pass any delegate that can throw the EF exception.
/// </summary>
public static class ConcurrencyGuard
{
    public static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException("This record was changed by someone else — please refresh and try again.", ex);
        }
    }
}
