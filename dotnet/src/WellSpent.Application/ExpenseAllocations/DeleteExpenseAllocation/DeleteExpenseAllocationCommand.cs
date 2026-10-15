using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.ExpenseAllocations.DeleteExpenseAllocation;

public sealed record DeleteExpenseAllocationCommand(Guid UserId, int Id, Guid BudgetProfileId) : IRequest;

/// <summary>Mirrors Go's bare DELETE exactly: a no-op, not a 404, when id doesn't belong to budgetProfileId.</summary>
public sealed class DeleteExpenseAllocationCommandHandler(BudgetAccessGuard access, IExpenseAllocationRepository allocations)
    : IRequestHandler<DeleteExpenseAllocationCommand>
{
    public async Task Handle(DeleteExpenseAllocationCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);
        await allocations.DeleteAsync(request.Id, request.BudgetProfileId, ct);
    }
}
