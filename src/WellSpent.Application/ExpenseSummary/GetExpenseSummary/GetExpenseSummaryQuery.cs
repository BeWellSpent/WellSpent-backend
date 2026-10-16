using MediatR;
using WellSpent.Application.Common;
using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.ExpenseSummary.GetExpenseSummary;

public sealed record GetExpenseSummaryQuery(Guid UserId, Guid BudgetPeriodId, bool FocusedView) : IRequest<GetExpenseSummaryDto>;

/// <summary>
/// Mirrors Go's ExpenseSummaryService.GetSummary: resolve the period, check
/// membership, load every input fresh (no caching/storage), optionally scope
/// it to the caller's own person via ApplyFocusedView, then hand it to the
/// pure ExpenseSummaryCalculator.
/// </summary>
public sealed class GetExpenseSummaryQueryHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions,
    IExpenseAllocationRepository allocations, IFixedExpenseRepository fixedExpenses)
    : IRequestHandler<GetExpenseSummaryQuery, GetExpenseSummaryDto>
{
    private const string SavingsSystemKey = "savings";

    public async Task<GetExpenseSummaryDto> Handle(GetExpenseSummaryQuery request, CancellationToken ct)
    {
        var period = await profiles.GetPeriodByIdAsync(request.BudgetPeriodId, ct);
        await access.EnsureMemberAsync(period.BudgetProfileId, request.UserId, ct);

        var systemCategories = await transactions.ListSystemCategoriesAsync(ct);
        var nonSpend = SpendFilter.NonSpendCategoryIds(systemCategories);
        int? savingsCategoryId = systemCategories.TryGetValue(SavingsSystemKey, out var savingsId) ? savingsId : null;

        var profileId = period.BudgetProfileId;
        var rawTransactions = await transactions.ListTransactionsAsync(period.Id, null, null, null, ct);

        var data = new ExpenseSummaryData
        {
            Period = period,
            People = await profiles.ListPeopleAsync(profileId, ct),
            Allocations = await allocations.ListAsync(profileId, ct),
            SavingsSources = await profiles.ListSavingsSourcesAsync(profileId, ct),
            ActiveFixedExpenses = await fixedExpenses.ListAsync(profileId, ct),
            IncomeSources = await profiles.ListIncomeSourcesAsync(profileId, ct),
            IncomeEntries = await profiles.ListIncomeEntriesAsync(period.Id, ct),
            PaymentMethods = await transactions.ListPaymentMethodsAsync(profileId, ct),
            Transactions = rawTransactions.Where(t => !SpendFilter.IsNonSpendTransaction(t, nonSpend)).ToList(),
            SavingsCategoryId = savingsCategoryId,
            Now = DateOnly.FromDateTime(DateTime.UtcNow),
        };

        if (request.FocusedView)
        {
            var myPerson = data.People.FirstOrDefault(p => p.UserId == request.UserId);
            if (myPerson is not null) ApplyFocusedView(data, myPerson.Id);
        }

        return new ExpenseSummaryCalculator(data).Response();
    }

    /// <summary>Restricts data in place to rows attributed to myPersonId plus unattributed ones, before the calculator ever sees it — every total downstream is scoped for free.</summary>
    private static void ApplyFocusedView(ExpenseSummaryData data, int myPersonId)
    {
        var pmPersonMap = data.PaymentMethods
            .Where(pm => pm.BudgetPersonId is not null)
            .ToDictionary(pm => pm.Id, pm => pm.BudgetPersonId!.Value);

        bool Mine(int? personId) => personId is null || personId == myPersonId;
        bool MineViaMethod(Guid? methodId) =>
            methodId is null || !pmPersonMap.TryGetValue(methodId.Value, out var personId) || personId == myPersonId;

        data.Transactions = data.Transactions.Where(t => MineViaMethod(t.PaymentMethodId)).ToList();
        data.Allocations = data.Allocations.Where(a => Mine(a.BudgetPersonId)).ToList();
        data.SavingsSources = data.SavingsSources.Where(s => Mine(s.BudgetPersonId)).ToList();
        data.ActiveFixedExpenses = data.ActiveFixedExpenses.Where(fe => MineViaMethod(fe.PaymentMethodId)).ToList();
        data.IncomeSources = data.IncomeSources.Where(s => Mine(s.BudgetPersonId)).ToList();
        data.IncomeEntries = data.IncomeEntries.Where(e => Mine(e.BudgetPersonId)).ToList();
    }
}
