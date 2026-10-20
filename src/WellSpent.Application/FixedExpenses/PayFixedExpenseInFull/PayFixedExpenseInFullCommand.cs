using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.Transactions;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses.PayFixedExpenseInFull;

public sealed record PayFixedExpenseInFullCommand(Guid UserId, Guid BudgetProfileId, Guid FixedExpenseId, decimal Amount, DateOnly PaidDate)
    : IRequest<PayFixedExpenseInFullResult>;

public sealed record PayFixedExpenseInFullResult(TransactionDto Transaction, FixedExpenseDto Expense);

/// <summary>
/// Settles a payment plan's remaining balance in one payment (issue #55).
/// Marks the current period's existing unpaid transaction paid at the entered
/// amount when there is one; otherwise creates a new paid transaction dated
/// PaidDate. Always passes autoSyncTemplate: false to the shared mark-paid
/// path — a payoff figure is a one-time settlement, not a new recurring
/// PlannedAmount — but the completion check inside that shared path still
/// runs regardless, so EndDate is never touched and the plan deactivates the
/// same way no matter which path crossed the threshold.
/// </summary>
public sealed class PayFixedExpenseInFullCommandHandler(
    BudgetAccessGuard access, IFixedExpenseRepository fixedExpenses, IBudgetProfileRepository profiles,
    ITransactionRepository transactions, ILogger<PayFixedExpenseInFullCommandHandler> logger)
    : IRequestHandler<PayFixedExpenseInFullCommand, PayFixedExpenseInFullResult>
{
    public async Task<PayFixedExpenseInFullResult> Handle(PayFixedExpenseInFullCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var fe = await fixedExpenses.GetByIdAsync(request.FixedExpenseId, ct);
        if (fe.BudgetProfileId != request.BudgetProfileId)
        {
            throw new ForbiddenException("access denied");
        }
        if (fe.TotalPayments is not { } total || total <= 0)
        {
            throw new AppValidationException("this fixed expense has no payment plan to pay off");
        }

        var existing = await fixedExpenses.GetTransactionAsync(fe.Id, request.BudgetProfileId, ct);

        Transaction tx;
        if (existing is not null && !existing.IsPaid && existing.BudgetPeriodId is { } existingPeriodId)
        {
            tx = await FixedExpensePaymentSync.MarkPaidAsync(
                transactions, fixedExpenses, existing.Id, existingPeriodId, request.Amount, request.PaidDate,
                autoSyncTemplate: false, default, logger, ct);
        }
        else
        {
            var period = await profiles.GetLatestPeriodAsync(request.BudgetProfileId, ct);
            tx = await transactions.CreateTransactionAsync(new Transaction
            {
                Name = fe.Name,
                Amount = request.Amount,
                PlannedAmount = request.Amount,
                Date = request.PaidDate,
                PaidDate = request.PaidDate,
                IsPaid = true,
                BudgetPeriodId = period.Id,
                CategoryId = fe.CategoryId,
                PaymentMethodId = fe.PaymentMethodId,
                TransactionTypeId = 1,
                FixedExpenseId = fe.Id,
            }, ct);

            await FixedExpensePayoff.CheckAndApplyAsync(fixedExpenses, transactions, tx, fe, request.PaidDate, logger, ct);
        }

        var updatedFe = await fixedExpenses.GetByIdAsync(fe.Id, ct);
        var amountPaid = await FixedExpensePayoff.AmountPaidAsync(fixedExpenses, updatedFe, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        logger.LogInformation(
            "fixed_expense.pay_in_full fixed_expense_id={FixedExpenseId} transaction_id={TransactionId} amount={Amount} paid_date={PaidDate}",
            fe.Id, tx.Id, request.Amount, request.PaidDate);

        return new PayFixedExpenseInFullResult(TransactionMapping.ToDto(tx), FixedExpenseMapping.ToDto(updatedFe, today, amountPaid));
    }
}
