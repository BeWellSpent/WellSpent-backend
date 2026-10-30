using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.ConfirmPeriodReview;

/// <summary>Admin-only. No-op if NeedsReview is already false.</summary>
public sealed record ConfirmPeriodReviewCommand(Guid UserId, Guid Id) : IRequest<BudgetPeriodDto>;

public sealed class ConfirmPeriodReviewCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<ConfirmPeriodReviewCommand, BudgetPeriodDto>
{
    public async Task<BudgetPeriodDto> Handle(ConfirmPeriodReviewCommand request, CancellationToken ct)
    {
        var period = await access.EnsureAdminOfPeriodAsync(request.Id, request.UserId, ct);
        if (period.NeedsReview)
        {
            await profiles.SetPeriodNeedsReviewAsync(period.Id, false, ct);
            period.NeedsReview = false;
        }
        return mapper.Map<BudgetPeriodDto>(period);
    }
}
