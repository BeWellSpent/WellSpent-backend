using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.Reports;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.FixedExpenses.GetFixedExpenseProjection;

public sealed record GetFixedExpenseProjectionQuery(Guid UserId, Guid BudgetProfileId, bool FocusedView = false) : IRequest<FixedExpenseProjectionDto>;

public sealed record FixedExpenseDropDto(DateOnly EndsOn, Money Amount, List<string> Names);

public sealed record FixedExpenseProjectionDto(List<FixedExpenseDropDto>? Projection, bool Locked);

/// <summary>When the fixed baseline drops as payment plans end, grouped by month (Pro only).</summary>
public sealed class GetFixedExpenseProjectionQueryHandler(
    BudgetAccessGuard access, IFixedExpenseRepository fixedExpenses, IBudgetProfileRepository profiles, IUserRepository users,
    ILogger<GetFixedExpenseProjectionQueryHandler> logger)
    : IRequestHandler<GetFixedExpenseProjectionQuery, FixedExpenseProjectionDto>
{
    private const int ProjectionMonths = 6;

    public async Task<FixedExpenseProjectionDto> Handle(GetFixedExpenseProjectionQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        if (!ReportRules.IsPro(await users.GetByIdAsync(request.UserId, ct)))
        {
            logger.LogInformation("fixed-expense projection locked budgetProfileId={BudgetProfileId} userId={UserId}", request.BudgetProfileId, request.UserId);
            return new FixedExpenseProjectionDto(null, true);
        }

        var focusedPersonId = await FocusedPerson.ResolveAsync(profiles, request.BudgetProfileId, request.UserId, request.FocusedView, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var projection = (await fixedExpenses.ListAsync(request.BudgetProfileId, ct, focusedPersonId) ?? [])
            .Where(fe => IsPlan(fe) && fe.EndDate is { } end && end >= today)
            .GroupBy(fe => new DateOnly(fe.EndDate!.Value.Year, fe.EndDate.Value.Month, 1))
            .OrderBy(g => g.Key)
            .Take(ProjectionMonths)
            .Select(g => new FixedExpenseDropDto(g.Key, Money.FromDecimal(g.Sum(fe => fe.PlannedAmount)), g.Select(fe => fe.Name).ToList()))
            .ToList();
        return new FixedExpenseProjectionDto(projection, false);
    }

    /// <summary>A template is a plan when it ends: installments, a payment count, or an end date.</summary>
    internal static bool IsPlan(FixedExpense fe) => fe.IsInstallmentPlan || (fe.TotalPayments ?? 0) > 0 || fe.EndDate is not null;
}
