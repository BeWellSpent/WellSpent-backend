using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>
/// Fills transaction.flow on save for every write path unless the caller set it: a new row
/// with none is spend or refund by its sign, and an amount change re-signs spend/refund only.
/// Categories never decide it (issue #119).
/// </summary>
internal static class TransactionFlowDerivation
{
    public static void Apply(WellSpentDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries<Transaction>())
        {
            var tx = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                tx.Flow ??= TransactionFlow.BySign(tx.Amount);
            }
            else if (entry.State == EntityState.Modified
                && !entry.Property(t => t.Flow).IsModified
                && entry.Property(t => t.Amount).IsModified)
            {
                tx.Flow = TransactionFlow.OnAmountChange(tx.Flow, tx.Amount);
            }
        }
    }
}
