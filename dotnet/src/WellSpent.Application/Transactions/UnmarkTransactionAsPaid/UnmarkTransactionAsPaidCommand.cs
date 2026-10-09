using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Transactions.UnmarkTransactionAsPaid;

/// <summary>
/// Mirrors Go's UnmarkTransactionAsPaid for the transaction row itself.
/// Deferred via HOOK: Go also undoes every *confirmed* TransactionReview
/// against this transaction (drops the fixed-expense alias, un-excludes the
/// imported variable transaction, resets the review) — not possible yet
/// (TransactionReview is B5 batch 7). Go treats every step of that cleanup
/// as best-effort/logged-and-continued, so its absence here doesn't change
/// whether the unmark itself succeeds.
/// </summary>
public sealed record UnmarkTransactionAsPaidCommand(Guid UserId, Guid Id, Guid BudgetPeriodId) : IRequest<TransactionDto>;

public sealed class UnmarkTransactionAsPaidCommandHandler(BudgetAccessGuard access, ITransactionRepository transactions)
    : IRequestHandler<UnmarkTransactionAsPaidCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(UnmarkTransactionAsPaidCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOfPeriodAsync(request.BudgetPeriodId, request.UserId, ct);
        var tx = await transactions.UnmarkTransactionAsPaidAsync(request.Id, request.BudgetPeriodId, ct);

        // HOOK: undo confirmed TransactionReview rows against this transaction (B5 batch 7).

        return TransactionMapping.ToDto(tx);
    }
}
