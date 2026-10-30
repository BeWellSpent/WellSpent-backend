using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.RetriggerPeriodReview;

/// <summary>
/// Admin-only. Deletes this period's carried-over transactions and re-derives
/// them from the previous period's current data — doesn't touch NeedsReview,
/// spawned Fixed-expense rows, or savings rows. No-op if there's no previous
/// period (same idempotency PeriodCarryover already has for "nothing to carry").
/// </summary>
public sealed record RetriggerPeriodReviewCommand(Guid UserId, Guid Id) : IRequest<BudgetPeriodDto>;

public sealed class RetriggerPeriodReviewCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions,
    ILogger<RetriggerPeriodReviewCommandHandler> logger, IMapper mapper)
    : IRequestHandler<RetriggerPeriodReviewCommand, BudgetPeriodDto>
{
    public async Task<BudgetPeriodDto> Handle(RetriggerPeriodReviewCommand request, CancellationToken ct)
    {
        var period = await access.EnsureAdminOfPeriodAsync(request.Id, request.UserId, ct);
        var profile = await profiles.GetByIdAsync(period.BudgetProfileId, ct);

        var allPeriods = await profiles.ListPeriodsAsync(profile.Id, ct);
        var previous = allPeriods
            .Where(p => p.StartDate < period.StartDate)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefault();

        if (previous is not null)
        {
            await transactions.DeleteCarriedTransactionsAsync(period.Id, previous.Id, ct);
            await PeriodCarryover.ApplyAsync(profiles, transactions, logger, profile, previous, period, ct);
        }

        return mapper.Map<BudgetPeriodDto>(period);
    }
}
