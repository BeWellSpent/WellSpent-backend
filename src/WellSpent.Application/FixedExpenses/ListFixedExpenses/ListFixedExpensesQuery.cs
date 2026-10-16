using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.FixedExpenses.ListFixedExpenses;

public sealed record ListFixedExpensesQuery(Guid UserId, Guid BudgetProfileId) : IRequest<List<FixedExpenseDto>>;

public sealed class ListFixedExpensesQueryHandler(BudgetAccessGuard access, IFixedExpenseRepository fixedExpenses)
    : IRequestHandler<ListFixedExpensesQuery, List<FixedExpenseDto>>
{
    public async Task<List<FixedExpenseDto>> Handle(ListFixedExpensesQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await fixedExpenses.ListAsync(request.BudgetProfileId, ct);
        return rows.Select(fe => FixedExpenseMapping.ToDto(fe, today)).ToList();
    }
}
