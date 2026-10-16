using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.ListIncomeSources;

public sealed record ListIncomeSourcesQuery(Guid UserId, Guid BudgetProfileId) : IRequest<List<IncomeSourceDto>>;

public sealed class ListIncomeSourcesQueryHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles)
    : IRequestHandler<ListIncomeSourcesQuery, List<IncomeSourceDto>>
{
    public async Task<List<IncomeSourceDto>> Handle(ListIncomeSourcesQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var rows = await profiles.ListIncomeSourcesAsync(request.BudgetProfileId, ct);
        return rows.Select(IncomeSourceMapping.ToDto).ToList();
    }
}
