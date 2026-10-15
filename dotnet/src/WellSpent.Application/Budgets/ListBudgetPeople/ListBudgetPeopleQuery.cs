using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.ListBudgetPeople;

public sealed record ListBudgetPeopleQuery(Guid UserId, Guid BudgetProfileId) : IRequest<List<BudgetPersonDto>>;

public sealed class ListBudgetPeopleQueryHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<ListBudgetPeopleQuery, List<BudgetPersonDto>>
{
    public async Task<List<BudgetPersonDto>> Handle(ListBudgetPeopleQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var rows = await profiles.ListPeopleAsync(request.BudgetProfileId, ct);
        return mapper.Map<List<BudgetPersonDto>>(rows);
    }
}
