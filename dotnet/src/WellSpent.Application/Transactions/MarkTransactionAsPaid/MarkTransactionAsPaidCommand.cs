using MediatR;
using WellSpent.Application.Common;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Transactions.MarkTransactionAsPaid;

/// <summary>
/// Mirrors Go's MarkTransactionAsPaid/markFixedTransactionPaid. Completes
/// batch 4's own deferred HOOK now that FixedExpense exists: when the
/// budget's auto_update_planned_amount is on, the linked template's planned
/// amount, due date, and category/payment method (falling back to the
/// transaction's own fields when nothing better was observed — there's no
/// separate Plaid import to observe from on the plain manual button) are
/// brought up to date with what actually happened. That sync is itself
/// best-effort in Go — a failure there never un-pays the transaction — so
/// it's wrapped the same way here.
///
/// Still deferred: Go's confirmTransactionMatch-adjacent review refresh after
/// a mark-paid via the Plaid auto-confirm path doesn't apply to this
/// RPC — this is the plain manual button, which never had a separate
/// TransactionReview step to begin with.
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

        var tx = await transactions.MarkTransactionAsPaidAsync(
            request.Id, request.BudgetPeriodId, request.PaidAmount.ToDecimal(), request.PaidDate, ct);

        if (tx.FixedExpenseId is { } feId && await AutoUpdatePlannedAmountAsync(period.BudgetProfileId, ct))
        {
            try
            {
                var fe = await fixedExpenses.GetByIdAsync(feId, ct);

                // No separate import to observe from — the manual button lets
                // the paid transaction's own (possibly user-edited) fields
                // speak for it instead.
                var categoryId = tx.CategoryId ?? fe.CategoryId;
                var paymentMethodId = tx.PaymentMethodId ?? fe.PaymentMethodId;

                var (dayOfMonth, dayOfWeek, anchorDate) = FixedExpenseScheduling.ScheduleFromAnchor(request.PaidDate);

                await fixedExpenses.UpdateFromPaymentAsync(
                    feId, request.PaidAmount.ToDecimal(), dayOfMonth, dayOfWeek, anchorDate, categoryId, paymentMethodId, ct);
            }
            catch (NotFoundException)
            {
                // Not fatal: the payment is recorded and correct. Only the
                // template missed the update — a wrong template, not a wrong
                // payment, and the user can edit it by hand.
            }
        }

        return TransactionMapping.ToDto(tx);
    }

    /// <summary>Defaults to true when the profile can't be read, matching the column default and the behavior every path had before this setting existed.</summary>
    private async Task<bool> AutoUpdatePlannedAmountAsync(Guid budgetProfileId, CancellationToken ct)
    {
        try
        {
            var profile = await profiles.GetByIdAsync(budgetProfileId, ct);
            return profile.AutoUpdatePlannedAmount;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
