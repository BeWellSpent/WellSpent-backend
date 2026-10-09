using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Transactions.DeleteTransaction;

public sealed record DeleteTransactionCommand(Guid UserId, Guid Id) : IRequest;

public sealed class DeleteTransactionCommandHandler(BudgetAccessGuard access, ITransactionRepository transactions)
    : IRequestHandler<DeleteTransactionCommand>
{
    public async Task Handle(DeleteTransactionCommand request, CancellationToken ct)
    {
        var tx = await transactions.GetTransactionAsync(request.Id, ct);
        if (tx.BudgetPeriodId is { } periodId)
        {
            await access.EnsureCollaboratorOfPeriodAsync(periodId, request.UserId, ct);
        }

        // Synced bank data is hard to recover once deleted — block entirely, same as edits.
        if (tx.PlaidTransactionId is not null)
        {
            throw new AppValidationException("Plaid-imported transactions cannot be deleted");
        }

        await transactions.DeleteTransactionAsync(request.Id, tx.BudgetPeriodId, ct);
    }
}
