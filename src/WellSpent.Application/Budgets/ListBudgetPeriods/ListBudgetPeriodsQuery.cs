using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.ListBudgetPeriods;

public sealed record ListBudgetPeriodsQuery(Guid UserId, Guid BudgetProfileId) : IRequest<List<BudgetPeriodDto>>;

public sealed class ListBudgetPeriodsQueryHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<ListBudgetPeriodsQuery, List<BudgetPeriodDto>>
{
    public async Task<List<BudgetPeriodDto>> Handle(ListBudgetPeriodsQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var rows = await profiles.ListPeriodsAsync(request.BudgetProfileId, ct);
        return mapper.Map<List<BudgetPeriodDto>>(rows);
    }
}
