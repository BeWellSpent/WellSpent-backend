using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

/// <summary>
/// Derives transaction.flow on save for every write path (create, edit, bill spawns,
/// Plaid sync, fixed-expense edits), unless the caller set it explicitly — so no path
/// can forget it and totals keep matching the category-based rule they replaced.
/// </summary>
internal static class TransactionFlowDerivation
{
    public static async Task ApplyAsync(WellSpentDbContext db, CancellationToken ct)
    {
        var entries = db.ChangeTracker.Entries<Transaction>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();
        if (entries.Count == 0) return;

        Dictionary<int, string>? keys = null;
        async Task<string?> KeyOf(int? categoryId)
        {
            if (categoryId is not { } id) return null;
            keys ??= await db.Categories.AsNoTracking()
                .Where(c => c.IsSystem && c.SystemKey != null)
                .ToDictionaryAsync(c => c.Id, c => c.SystemKey!, ct);
            return keys.GetValueOrDefault(id);
        }

        foreach (var entry in entries)
        {
            var tx = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                tx.Flow ??= TransactionFlow.Infer(await KeyOf(tx.CategoryId), tx.Amount);
                continue;
            }
            if (Explicit(entry)) continue;

            var category = entry.Property(t => t.CategoryId);
            if (category.IsModified && category.OriginalValue != category.CurrentValue)
            {
                tx.Flow = TransactionFlow.OnCategoryChange(tx.Flow, await KeyOf(category.OriginalValue), await KeyOf(tx.CategoryId), tx.Amount);
            }
            else if (entry.Property(t => t.Amount).IsModified)
            {
                tx.Flow = TransactionFlow.OnAmountChange(tx.Flow, tx.Amount);
            }
        }
    }

    private static bool Explicit(EntityEntry<Transaction> entry) => entry.Property(t => t.Flow).IsModified;
}
