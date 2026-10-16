using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class FixedExpenseRepository(WellSpentDbContext db) : IFixedExpenseRepository
{
    public async Task<FixedExpense> CreateAsync(FixedExpense fixedExpense, CancellationToken ct)
    {
        db.FixedExpenses.Add(fixedExpense);
        await db.SaveChangesAsync(ct);
        return fixedExpense;
    }

    public async Task<FixedExpense> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.FixedExpenses.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundException("fixed_expense", id.ToString());

    public async Task<List<FixedExpense>> ListAsync(Guid budgetProfileId, CancellationToken ct) =>
        await db.FixedExpenses.Where(f => f.BudgetProfileId == budgetProfileId && f.IsActive)
            .OrderBy(f => f.Name).ToListAsync(ct);

    public async Task<FixedExpense> UpdateAsync(FixedExpense fixedExpense, CancellationToken ct)
    {
        var existing = await db.FixedExpenses.FirstOrDefaultAsync(
            f => f.Id == fixedExpense.Id && f.BudgetProfileId == fixedExpense.BudgetProfileId, ct)
            ?? throw new NotFoundException("fixed_expense", fixedExpense.Id.ToString());
        existing.Name = fixedExpense.Name;
        existing.PlannedAmount = fixedExpense.PlannedAmount;
        existing.CategoryId = fixedExpense.CategoryId;
        existing.PaymentMethodId = fixedExpense.PaymentMethodId;
        existing.DayOfMonth = fixedExpense.DayOfMonth;
        existing.IntervalMonths = fixedExpense.IntervalMonths;
        existing.AnchorDate = fixedExpense.AnchorDate;
        existing.FrequencyUnit = fixedExpense.FrequencyUnit;
        existing.IntervalWeeks = fixedExpense.IntervalWeeks;
        existing.DayOfWeek = fixedExpense.DayOfWeek;
        existing.EndDate = fixedExpense.EndDate;
        existing.TotalPayments = fixedExpense.TotalPayments;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task UpdateFromPaymentAsync(
        Guid id, decimal plannedAmount, int dayOfMonth, int dayOfWeek, DateOnly? anchorDate,
        int? categoryId, Guid? paymentMethodId, CancellationToken ct)
    {
        var fe = await db.FixedExpenses.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fe is null) return;
        fe.PlannedAmount = plannedAmount;
        fe.DayOfMonth = dayOfMonth;
        fe.DayOfWeek = (short)dayOfWeek;
        fe.AnchorDate = anchorDate;
        fe.CategoryId = categoryId;
        fe.PaymentMethodId = paymentMethodId;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, Guid budgetProfileId, CancellationToken ct)
    {
        await db.FixedExpenses.Where(f => f.Id == id && f.BudgetProfileId == budgetProfileId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsActive, false), ct);
    }

    public async Task<bool> HasTransactionInMonthAsync(Guid fixedExpenseId, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct) =>
        await db.Transactions.AnyAsync(t =>
            t.FixedExpenseId == fixedExpenseId && t.Date >= monthStart && t.Date < monthEnd, ct);

    public async Task<bool> HasTransactionOnDateAsync(Guid fixedExpenseId, DateOnly targetDate, CancellationToken ct) =>
        await db.Transactions.AnyAsync(t => t.FixedExpenseId == fixedExpenseId && t.Date == targetDate, ct);

    public async Task<Transaction?> GetTransactionAsync(Guid fixedExpenseId, Guid budgetProfileId, CancellationToken ct) =>
        await db.Transactions
            .Where(t => t.FixedExpenseId == fixedExpenseId &&
                db.BudgetPeriods.Any(bp => bp.Id == t.BudgetPeriodId && bp.BudgetProfileId == budgetProfileId && !bp.IsArchived))
            .OrderBy(t => t.IsPaid).ThenByDescending(t => t.Date)
            .FirstOrDefaultAsync(ct);

    public async Task DeleteUnpaidTransactionsAsync(Guid fixedExpenseId, Guid budgetProfileId, CancellationToken ct)
    {
        await db.Transactions
            .Where(t => t.FixedExpenseId == fixedExpenseId && !t.IsPaid &&
                db.BudgetPeriods.Any(bp => bp.Id == t.BudgetPeriodId && bp.BudgetProfileId == budgetProfileId && !bp.IsArchived))
            .ExecuteDeleteAsync(ct);
    }

    public async Task UpdateTransactionFromFixedExpenseAsync(
        Guid fixedExpenseId, Guid budgetProfileId, string name, decimal plannedAmount,
        int? categoryId, Guid? paymentMethodId, DateOnly date, CancellationToken ct)
    {
        var tx = await db.Transactions
            .Where(t => t.FixedExpenseId == fixedExpenseId && !t.IsPaid &&
                db.BudgetPeriods.Any(bp => bp.Id == t.BudgetPeriodId && bp.BudgetProfileId == budgetProfileId && !bp.IsArchived))
            .FirstOrDefaultAsync(ct);
        if (tx is null) return;
        tx.Name = name;
        tx.PlannedAmount = plannedAmount;
        tx.Amount = plannedAmount;
        tx.CategoryId = categoryId;
        tx.PaymentMethodId = paymentMethodId;
        tx.Date = date;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdatePaidTransactionFromFixedExpenseAsync(
        Guid fixedExpenseId, Guid budgetProfileId, string name, int? categoryId, Guid? paymentMethodId, CancellationToken ct)
    {
        var tx = await db.Transactions
            .Where(t => t.FixedExpenseId == fixedExpenseId && t.IsPaid &&
                db.BudgetPeriods.Any(bp => bp.Id == t.BudgetPeriodId && bp.BudgetProfileId == budgetProfileId && !bp.IsArchived))
            .FirstOrDefaultAsync(ct);
        if (tx is null) return;
        tx.Name = name;
        tx.CategoryId = categoryId;
        tx.PaymentMethodId = paymentMethodId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Transaction?> GetUnpaidTransactionInPeriodAsync(Guid fixedExpenseId, Guid budgetPeriodId, CancellationToken ct) =>
        await db.Transactions
            .Where(t => t.FixedExpenseId == fixedExpenseId && !t.IsPaid && t.BudgetPeriodId == budgetPeriodId)
            .OrderByDescending(t => t.Date)
            .FirstOrDefaultAsync(ct);
}
