using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Transactions.MarkTransactionAsPaid;

/// <summary>
/// Mirrors Go's MarkTransactionAsPaid/markFixedTransactionPaid for the
/// transaction row itself. Deferred via HOOK: when auto_update_planned_amount
/// is on, Go also syncs the linked FixedExpense template's planned amount,
/// due date, and observed category/payment method — not possible yet
/// (FixedExpense is B5 batch 5). That sync is itself best-effort/non-fatal in
/// Go (a failure there never un-pays the transaction), so its absence here
/// changes nothing about whether this call succeeds — only whether next
/// period's plan reflects what was actually paid.
/// </summary>
public sealed record MarkTransactionAsPaidCommand(Guid UserId, Guid Id, Guid BudgetPeriodId, Money PaidAmount, DateOnly PaidDate)
    : IRequest<TransactionDto>;

public sealed class MarkTransactionAsPaidCommandHandler(BudgetAccessGuard access, ITransactionRepository transactions)
    : IRequestHandler<MarkTransactionAsPaidCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(MarkTransactionAsPaidCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOfPeriodAsync(request.BudgetPeriodId, request.UserId, ct);

        var tx = await transactions.MarkTransactionAsPaidAsync(
            request.Id, request.BudgetPeriodId, request.PaidAmount.ToDecimal(), request.PaidDate, ct);

        // HOOK: sync the linked FixedExpense template when auto_update_planned_amount is on (B5 batch 5).

        return TransactionMapping.ToDto(tx);
    }
}
