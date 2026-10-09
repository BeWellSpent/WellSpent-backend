using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class TransactionRepository(WellSpentDbContext db) : ITransactionRepository
{
    // ── Categories ───────────────────────────────────────────────────────────

    public async Task<Category> GetCategoryAsync(int id, CancellationToken ct) =>
        await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("category", id.ToString());

    public async Task<List<Category>> ListCategoriesAsync(Guid userId, CancellationToken ct) =>
        await db.Categories
            .Where(c => (c.UserId == userId && c.IsActive) || c.UserId == null)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<List<Category>> ListCategoriesForBudgetAsync(Guid userId, Guid budgetProfileId, CancellationToken ct)
    {
        var ids = await db.Database.SqlQuery<int>(
            $"""
            SELECT DISTINCT c.id AS "Value"
            FROM category c
            WHERE (c.user_id = {userId} AND c.is_active = TRUE)
               OR c.user_id IS NULL
               OR c.id IN (
                 SELECT DISTINCT t.category_id FROM transaction t
                 JOIN budget_period bp ON t.budget_period_id = bp.id
                 WHERE bp.budget_profile_id = {budgetProfileId} AND t.category_id IS NOT NULL
               )
               OR c.id IN (
                 SELECT DISTINCT fe.category_id FROM fixed_expense fe
                 WHERE fe.budget_profile_id = {budgetProfileId} AND fe.category_id IS NOT NULL
               )
            """).ToListAsync(ct);

        return await db.Categories.Where(c => ids.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<Category> CreateCategoryAsync(string name, Guid userId, string color, CancellationToken ct)
    {
        var expenseTypeId = await db.Database.SqlQuery<int>(
            $"""SELECT id AS "Value" FROM category_type WHERE name = 'Expense'""").FirstAsync(ct);

        var category = new Category { Name = name, UserId = userId, Color = color, TypeId = expenseTypeId };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task<Category> UpdateCategoryAsync(int id, Guid userId, string name, string color, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(
            c => c.Id == id && c.UserId == userId && !c.IsSystem, ct)
            ?? throw new NotFoundException("category", id.ToString());
        category.Name = name;
        category.Color = color;
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task<Category> UpdateSystemCategoryColorAsync(int id, string color, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.IsSystem, ct)
            ?? throw new NotFoundException("category", id.ToString());
        category.Color = color;
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task DeleteCategoryAndReassignAsync(int id, Guid userId, int replacementId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE transaction SET category_id = {replacementId} WHERE category_id = {id}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE fixed_expense SET category_id = {replacementId} WHERE category_id = {id}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE category SET is_active = FALSE WHERE id = {id} AND user_id = {userId} AND is_system = FALSE", ct);
        await tx.CommitAsync(ct);
    }

    // ── Payment methods ──────────────────────────────────────────────────────

    public async Task<PaymentMethod> GetPaymentMethodAsync(Guid id, CancellationToken ct) =>
        await db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("payment_method", id.ToString());

    public async Task<List<PaymentMethod>> ListPaymentMethodsAsync(Guid budgetProfileId, CancellationToken ct) =>
        await db.PaymentMethods
            .Where(m => m.IsActive && db.BudgetPeople.Any(p => p.Id == m.BudgetPersonId && p.BudgetProfileId == budgetProfileId))
            .OrderBy(m => m.Name)
            .ToListAsync(ct);

    public async Task<PaymentMethod> CreatePaymentMethodAsync(PaymentMethod method, CancellationToken ct)
    {
        db.PaymentMethods.Add(method);
        await db.SaveChangesAsync(ct);
        return method;
    }

    public async Task<PaymentMethod> UpdatePaymentMethodAsync(Guid id, string name, string color, string? alias, CancellationToken ct)
    {
        var method = await db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("payment_method", id.ToString());
        method.Name = name;
        method.Color = color;
        method.Alias = alias;
        await db.SaveChangesAsync(ct);
        return method;
    }

    public async Task DeletePaymentMethodAndReassignAsync(Guid id, Guid replacementId, Guid budgetProfileId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE transaction SET payment_method_id = {replacementId}
            WHERE payment_method_id = {id}
              AND budget_period_id IN (SELECT bp.id FROM budget_period bp WHERE bp.budget_profile_id = {budgetProfileId})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE savings_source SET payment_method_id = {replacementId}
            WHERE payment_method_id = {id} AND budget_profile_id = {budgetProfileId}
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE fixed_expense SET payment_method_id = {replacementId}
            WHERE payment_method_id = {id} AND budget_profile_id = {budgetProfileId}
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE payment_methods SET is_active = FALSE WHERE id = {id}", ct);
        await tx.CommitAsync(ct);
    }
}
