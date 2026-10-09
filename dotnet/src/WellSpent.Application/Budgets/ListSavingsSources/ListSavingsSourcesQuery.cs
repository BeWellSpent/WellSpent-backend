using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.ListSavingsSources;

public sealed record ListSavingsSourcesQuery(Guid UserId, Guid BudgetProfileId) : IRequest<List<SavingsSourceDto>>;

public sealed class ListSavingsSourcesQueryHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles)
    : IRequestHandler<ListSavingsSourcesQuery, List<SavingsSourceDto>>
{
    public async Task<List<SavingsSourceDto>> Handle(ListSavingsSourcesQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var rows = await profiles.ListSavingsSourcesAsync(request.BudgetProfileId, ct);
        return rows.Select(SavingsSourceMapping.ToDto).ToList();
    }
}
