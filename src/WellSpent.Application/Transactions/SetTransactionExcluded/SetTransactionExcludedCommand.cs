using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Transactions.SetTransactionExcluded;

public sealed record SetTransactionExcludedCommand(Guid UserId, Guid Id, Guid BudgetPeriodId, bool Excluded)
    : IRequest<TransactionDto>;

public sealed class SetTransactionExcludedCommandHandler(BudgetAccessGuard access, ITransactionRepository transactions)
    : IRequestHandler<SetTransactionExcludedCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(SetTransactionExcludedCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOfPeriodAsync(request.BudgetPeriodId, request.UserId, ct);
        var tx = await transactions.SetTransactionExcludedAsync(request.Id, request.BudgetPeriodId, request.Excluded, ct);
        return TransactionMapping.ToDto(tx);
    }
}
