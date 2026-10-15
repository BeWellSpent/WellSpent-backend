using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses.DeleteFixedExpense;

public sealed record DeleteFixedExpenseCommand(Guid UserId, Guid Id, Guid BudgetProfileId) : IRequest;

public sealed class DeleteFixedExpenseCommandHandler(BudgetAccessGuard access, IFixedExpenseRepository fixedExpenses)
    : IRequestHandler<DeleteFixedExpenseCommand>
{
    public async Task Handle(DeleteFixedExpenseCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var fe = await fixedExpenses.GetByIdAsync(request.Id, ct);
        if (fe.BudgetProfileId != request.BudgetProfileId)
        {
            throw new ForbiddenException("access denied");
        }

        try
        {
            await fixedExpenses.DeleteUnpaidTransactionsAsync(request.Id, request.BudgetProfileId, ct);
        }
        catch (Exception)
        {
            // A deactivated template's unpaid bill stays on the period.
        }

        await fixedExpenses.DeactivateAsync(request.Id, request.BudgetProfileId, ct);
    }
}
