using MediatR;
using WellSpent.Application.Common;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Transactions.MarkTransactionAsPaid;

/// <summary>
/// Mirrors Go's MarkTransactionAsPaid, delegating the template-sync side
/// effect to FixedExpensePaymentSync.MarkPaidAsync — the same shared path
/// ConfirmTransactionReview and CreateFixedExpenseFromTransaction use (B5
/// batch 7). The plain manual button has no separate Plaid import to observe
/// category/payment method from, so it passes a default ObservedPayment and
/// lets the paid transaction's own (possibly user-edited) fields speak for it.
/// </summary>
public sealed record MarkTransactionAsPaidCommand(Guid UserId, Guid Id, Guid BudgetPeriodId, Money PaidAmount, DateOnly PaidDate)
    : IRequest<TransactionDto>;

public sealed class MarkTransactionAsPaidCommandHandler(
    BudgetAccessGuard access, ITransactionRepository transactions, IFixedExpenseRepository fixedExpenses, IBudgetProfileRepository profiles)
    : IRequestHandler<MarkTransactionAsPaidCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(MarkTransactionAsPaidCommand request, CancellationToken ct)
    {
        var period = await access.EnsureCollaboratorOfPeriodAsync(request.BudgetPeriodId, request.UserId, ct);

        var autoSync = await FixedExpensePaymentSync.AutoUpdatePlannedAmountForAsync(profiles, period.BudgetProfileId, ct);
        var tx = await FixedExpensePaymentSync.MarkPaidAsync(
            transactions, fixedExpenses, request.Id, request.BudgetPeriodId, request.PaidAmount.ToDecimal(), request.PaidDate,
            autoSync, default, ct);

        return TransactionMapping.ToDto(tx);
    }
}
