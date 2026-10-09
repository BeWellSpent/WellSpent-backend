using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.ExpenseAllocations.UpsertExpenseAllocation;

/// <summary>BudgetPersonId null = unattributed, matching every other request body in this codebase (e.g. AddIncomeSourceCommand) — the 0-sentinel is a response-only convention, mirrored on ExpenseAllocationDto to match the wire exactly.</summary>
public sealed record UpsertExpenseAllocationCommand(Guid UserId, Guid BudgetProfileId, int CategoryId, int? BudgetPersonId, Money PlannedAmount)
    : IRequest<ExpenseAllocationDto>;

public sealed class UpsertExpenseAllocationCommandHandler(BudgetAccessGuard access, IExpenseAllocationRepository allocations)
    : IRequestHandler<UpsertExpenseAllocationCommand, ExpenseAllocationDto>
{
    public async Task<ExpenseAllocationDto> Handle(UpsertExpenseAllocationCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);
        var allocation = await allocations.UpsertAsync(
            request.BudgetProfileId, request.CategoryId, request.BudgetPersonId, request.PlannedAmount.ToDecimal(), ct);
        return ExpenseAllocationMapping.ToDto(allocation);
    }
}
