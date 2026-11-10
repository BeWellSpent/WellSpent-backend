using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.ExpenseSummary;

/// <summary>A budget's profile-level summary inputs, loaded once and reused for every period a report covers.</summary>
public sealed record ExpenseSummaryProfileInputs(
    List<BudgetPerson> People, List<ExpenseAllocation> Allocations, List<SavingsSource> SavingsSources,
    List<FixedExpense> ActiveFixedExpenses, List<IncomeSource> IncomeSources, List<PaymentMethod> PaymentMethods,
    HashSet<int> NonSpend, int? SavingsCategoryId, int? IncomeCategoryId);

/// <summary>Loads ExpenseSummaryData: the expense summary for one period, the reports for many.</summary>
public sealed class ExpenseSummaryDataLoader(
    IBudgetProfileRepository profiles, ITransactionRepository transactions,
    IExpenseAllocationRepository allocations, IFixedExpenseRepository fixedExpenses)
{
    private const string SavingsSystemKey = "savings";
    private const string IncomeSystemKey = "income";

    public async Task<ExpenseSummaryProfileInputs> LoadProfileAsync(Guid profileId, CancellationToken ct)
    {
        var systemCategories = await transactions.ListSystemCategoriesAsync(ct) ?? [];
        return new ExpenseSummaryProfileInputs(
            await profiles.ListPeopleAsync(profileId, ct) ?? [],
            await allocations.ListAsync(profileId, ct) ?? [],
            await profiles.ListSavingsSourcesAsync(profileId, ct) ?? [],
            await fixedExpenses.ListAsync(profileId, ct) ?? [],
            (await profiles.ListIncomeSourcesAsync(profileId, ct) ?? []).Where(s => s.EffectiveTo is null).ToList(),
            await transactions.ListPaymentMethodsAsync(profileId, ct) ?? [],
            SpendFilter.NonSpendCategoryIds(systemCategories),
            systemCategories.TryGetValue(SavingsSystemKey, out var savingsId) ? savingsId : null,
            systemCategories.TryGetValue(IncomeSystemKey, out var incomeId) ? incomeId : null);
    }

    /// <summary>Fresh lists per call, since focused view mutates them in place.</summary>
    public async Task<ExpenseSummaryData> LoadPeriodAsync(BudgetPeriod period, ExpenseSummaryProfileInputs inputs, CancellationToken ct)
    {
        var rawTransactions = await transactions.ListTransactionsAsync(period.Id, null, null, null, ct) ?? [];
        return new ExpenseSummaryData
        {
            Period = period,
            People = inputs.People.ToList(),
            Allocations = inputs.Allocations.ToList(),
            SavingsSources = inputs.SavingsSources.ToList(),
            ActiveFixedExpenses = inputs.ActiveFixedExpenses.ToList(),
            IncomeSources = inputs.IncomeSources.ToList(),
            IncomeEntries = await profiles.ListIncomeEntriesAsync(period.Id, ct) ?? [],
            PaymentMethods = inputs.PaymentMethods.ToList(),
            Transactions = rawTransactions.Where(t => !SpendFilter.IsNonSpendTransaction(t, inputs.NonSpend)).ToList(),
            IncomeTransactions = inputs.IncomeCategoryId is { } incId
                ? rawTransactions.Where(t => !t.IsExcluded && t.CategoryId == incId).ToList()
                : [],
            SavingsCategoryId = inputs.SavingsCategoryId,
            Now = DateOnly.FromDateTime(DateTime.UtcNow),
        };
    }
}
