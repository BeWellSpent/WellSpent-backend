using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.GetBudgetPeriod;

public sealed record GetBudgetPeriodQuery(Guid UserId, Guid Id) : IRequest<BudgetPeriodDto>;

public sealed class GetBudgetPeriodQueryHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<GetBudgetPeriodQuery, BudgetPeriodDto>
{
    public async Task<BudgetPeriodDto> Handle(GetBudgetPeriodQuery request, CancellationToken ct)
    {
        var period = await profiles.GetPeriodByIdAsync(request.Id, ct);
        await access.EnsureMemberAsync(period.BudgetProfileId, request.UserId, ct);
        return mapper.Map<BudgetPeriodDto>(period);
    }
}
