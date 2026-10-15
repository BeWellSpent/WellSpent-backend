using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses;

/// <summary>
/// The create-template-then-spawn-into-the-active-period logic shared by
/// CreateFixedExpense and CreateInstallmentPlan (which literally calls
/// CreateFixedExpense in Go). Pulled out so both call sites run the access
/// check their own RPC actually requires exactly once, rather than nesting
/// one MediatR command inside another.
/// </summary>
public static class FixedExpenseCreation
{
    public static async Task<(FixedExpense Expense, Transaction? Transaction)> CreateAndSpawnAsync(
        IFixedExpenseRepository fixedExpenses, IBudgetProfileRepository profiles, ITransactionRepository transactions,
        Guid budgetProfileId, FixedExpenseFields fields, CancellationToken ct, bool isInstallmentPlan = false)
    {
        var entity = FixedExpenseNormalization.BuildEntity(fields);
        entity.BudgetProfileId = budgetProfileId;
        entity.IsInstallmentPlan = isInstallmentPlan;
        var fe = await fixedExpenses.CreateAsync(entity, ct);

        BudgetPeriod period;
        try
        {
            period = await profiles.GetLatestPeriodAsync(budgetProfileId, ct);
        }
        catch (NotFoundException)
        {
            return (fe, null);
        }

        var startDate = period.StartDate;

        if (FixedExpenseScheduling.IsWeekUnit(fe))
        {
            await FixedExpenseSpawning.SpawnWeeklyOccurrencesAsync(transactions, fixedExpenses, fe, period.Id, startDate, period.EndDate, null, ct);
            return (fe, null);
        }

        if (fe.AnchorDate is not null && !FixedExpenseScheduling.IsDueInMonth(fe, startDate))
        {
            return (fe, null); // future-dated; no transaction until due
        }

        try
        {
            var tx = await transactions.CreateTransactionAsync(new Transaction
            {
                Name = fe.Name,
                Amount = fe.PlannedAmount,
                PlannedAmount = fe.PlannedAmount,
                Date = FixedExpenseScheduling.DateInMonth(fe, startDate),
                BudgetPeriodId = period.Id,
                CategoryId = fe.CategoryId,
                PaymentMethodId = fe.PaymentMethodId,
                TransactionTypeId = 1,
                FixedExpenseId = fe.Id,
            }, ct);
            return (fe, tx);
        }
        catch (Exception)
        {
            return (fe, null);
        }
    }
}
