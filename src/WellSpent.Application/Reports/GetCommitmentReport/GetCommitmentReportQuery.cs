using MediatR;
using WellSpent.Application.Common;
using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Reports.GetCommitmentReport;

public sealed record GetCommitmentReportQuery(Guid UserId, Guid BudgetProfileId, long? BudgetPersonId) : IRequest<CommitmentReportDto>;

/// <summary>
/// What the budget owes before spending anything: the planned amounts of the
/// Fixed bills spawned into each of the last 6 periods, the current period by
/// category, and (Pro) when payment plans ending lowers that baseline.
/// </summary>
public sealed class GetCommitmentReportQueryHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions,
    IExpenseAllocationRepository allocations, IFixedExpenseRepository fixedExpenses, IUserRepository users)
    : IRequestHandler<GetCommitmentReportQuery, CommitmentReportDto>
{
    private const int PeriodCount = 6;
    private const int ProjectionMonths = 6;

    public async Task<CommitmentReportDto> Handle(GetCommitmentReportQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var isPro = ReportRules.IsPro(await users.GetByIdAsync(request.UserId, ct));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var inputs = await new ExpenseSummaryDataLoader(profiles, transactions, allocations, fixedExpenses)
            .LoadProfileAsync(request.BudgetProfileId, ct);
        var personByMethod = inputs.PaymentMethods
            .Where(pm => pm.BudgetPersonId is not null)
            .ToDictionary(pm => pm.Id, pm => (long)pm.BudgetPersonId!.Value);
        bool Mine(Guid? methodId) =>
            request.BudgetPersonId is not { } person
            || (methodId is { } id && personByMethod.TryGetValue(id, out var owner) && owner == person);

        var periods = ReportRules.RecentStarted(await profiles.ListPeriodsAsync(request.BudgetProfileId, ct) ?? [], today, PeriodCount);
        var periodDtos = new List<CommitmentPeriodDto>();
        List<Transaction> currentBills = [];
        for (var i = 0; i < periods.Count; i++)
        {
            var period = periods[i];
            var indexFromEnd = periods.Count - 1 - i;
            if (ReportRules.IsLocked(isPro, indexFromEnd))
            {
                periodDtos.Add(new CommitmentPeriodDto(period.Id, period.StartDate, period.EndDate, true, null));
                continue;
            }

            var bills = (await transactions.ListTransactionsAsync(period.Id, null, null, null, ct) ?? [])
                .Where(t => SpendFilter.IsFixed(t) && !SpendFilter.IsNonSpendTransaction(t, inputs.NonSpend) && Mine(t.PaymentMethodId))
                .ToList();
            periodDtos.Add(new CommitmentPeriodDto(period.Id, period.StartDate, period.EndDate, false, Money.FromDecimal(bills.Sum(t => t.PlannedAmount))));
            if (indexFromEnd == 0) currentBills = bills;
        }

        var incomePlanned = inputs.IncomeSources
            .Where(s => request.BudgetPersonId is not { } person || s.BudgetPersonId == person)
            .Sum(s => s.DefaultAmount);
        var committed = currentBills.Sum(t => t.PlannedAmount);
        var categories = currentBills
            .GroupBy(t => t.CategoryId ?? 0)
            .Select(g => (Id: g.Key, Amount: g.Sum(t => t.PlannedAmount)))
            .OrderByDescending(c => c.Amount)
            .Select(c => new CategoryAmountDto(c.Id, Money.FromDecimal(c.Amount), ReportRules.Percent(c.Amount, incomePlanned)))
            .ToList();

        var plans = inputs.ActiveFixedExpenses.Where(fe => ReportRules.IsPlan(fe) && Mine(fe.PaymentMethodId)).ToList();
        List<CommitmentDropDto>? projection = null;
        if (isPro)
        {
            projection = plans
                .Where(fe => fe.EndDate is { } end && end >= today)
                .GroupBy(fe => new DateOnly(fe.EndDate!.Value.Year, fe.EndDate.Value.Month, 1))
                .OrderBy(g => g.Key)
                .Take(ProjectionMonths)
                .Select(g => new CommitmentDropDto(g.Key, Money.FromDecimal(g.Sum(fe => fe.PlannedAmount)), g.Select(fe => fe.Name).ToList()))
                .ToList();
        }

        return new CommitmentReportDto(
            periodDtos, categories, Money.FromDecimal(incomePlanned), ReportRules.Percent(committed, incomePlanned),
            currentBills.Count, plans.Count, projection, !isPro);
    }
}
