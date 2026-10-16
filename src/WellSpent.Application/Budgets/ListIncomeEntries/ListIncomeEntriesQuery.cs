using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.ListIncomeEntries;

public sealed record ListIncomeEntriesQuery(Guid UserId, Guid BudgetPeriodId) : IRequest<List<IncomeEntryDto>>;

public sealed class ListIncomeEntriesQueryHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles)
    : IRequestHandler<ListIncomeEntriesQuery, List<IncomeEntryDto>>
{
    public async Task<List<IncomeEntryDto>> Handle(ListIncomeEntriesQuery request, CancellationToken ct)
    {
        var period = await profiles.GetPeriodByIdAsync(request.BudgetPeriodId, ct);
        await access.EnsureMemberAsync(period.BudgetProfileId, request.UserId, ct);
        var rows = await profiles.ListIncomeEntriesAsync(request.BudgetPeriodId, ct);
        return rows.Select(IncomeEntryMapping.ToDto).ToList();
    }
}
