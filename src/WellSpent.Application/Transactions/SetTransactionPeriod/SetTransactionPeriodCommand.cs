using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Transactions.SetTransactionPeriod;

public sealed record SetTransactionPeriodCommand(Guid UserId, Guid Id, Guid BudgetPeriodId, Guid NewBudgetPeriodId)
    : IRequest<TransactionDto>;

public sealed class SetTransactionPeriodCommandHandler(BudgetAccessGuard access, ITransactionRepository transactions)
    : IRequestHandler<SetTransactionPeriodCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(SetTransactionPeriodCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOfPeriodAsync(request.BudgetPeriodId, request.UserId, ct);
        // Also validates NewBudgetPeriodId exists (NotFoundException -> 404) and that the caller has access to it.
        var newPeriod = await access.EnsureCollaboratorOfPeriodAsync(request.NewBudgetPeriodId, request.UserId, ct);
        var tx = await transactions.SetTransactionPeriodOverrideAsync(request.Id, newPeriod.Id, ct);
        return TransactionMapping.ToDto(tx);
    }
}
