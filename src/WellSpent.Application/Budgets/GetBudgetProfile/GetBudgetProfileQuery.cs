using AutoMapper;
using MediatR;
using WellSpent.Application.Common;

namespace WellSpent.Application.Budgets.GetBudgetProfile;

public sealed record GetBudgetProfileQuery(Guid UserId, Guid Id) : IRequest<BudgetProfileDto>;

public sealed class GetBudgetProfileQueryHandler(BudgetAccessGuard access, IMapper mapper)
    : IRequestHandler<GetBudgetProfileQuery, BudgetProfileDto>
{
    public async Task<BudgetProfileDto> Handle(GetBudgetProfileQuery request, CancellationToken ct)
    {
        var profile = await access.EnsureMemberAsync(request.Id, request.UserId, ct);
        return mapper.Map<BudgetProfileDto>(profile);
    }
}
