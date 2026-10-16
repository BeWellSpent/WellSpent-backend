using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.ExpenseAllocations.ListExpenseAllocations;

public sealed record ListExpenseAllocationsQuery(Guid UserId, Guid BudgetProfileId) : IRequest<List<ExpenseAllocationDto>>;

public sealed class ListExpenseAllocationsQueryHandler(BudgetAccessGuard access, IExpenseAllocationRepository allocations)
    : IRequestHandler<ListExpenseAllocationsQuery, List<ExpenseAllocationDto>>
{
    public async Task<List<ExpenseAllocationDto>> Handle(ListExpenseAllocationsQuery request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var rows = await allocations.ListAsync(request.BudgetProfileId, ct);
        return rows.Select(ExpenseAllocationMapping.ToDto).ToList();
    }
}
