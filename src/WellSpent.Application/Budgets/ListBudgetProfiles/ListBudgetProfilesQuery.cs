using AutoMapper;
using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.ListBudgetProfiles;

public sealed record ListBudgetProfilesQuery(Guid UserId) : IRequest<List<BudgetProfileDto>>;

public sealed class ListBudgetProfilesQueryHandler(IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<ListBudgetProfilesQuery, List<BudgetProfileDto>>
{
    public async Task<List<BudgetProfileDto>> Handle(ListBudgetProfilesQuery request, CancellationToken ct)
    {
        var rows = await profiles.ListByUserOrMemberAsync(request.UserId, ct);
        return mapper.Map<List<BudgetProfileDto>>(rows);
    }
}
