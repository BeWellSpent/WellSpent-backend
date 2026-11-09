using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Transactions.MarkTransactionAsPaid;

/// <summary>
/// Mirrors Go's MarkTransactionAsPaid, delegating the template-sync side
/// effect to FixedExpensePaymentSync.MarkPaidAsync — the same shared path
/// ConfirmTransactionReview and CreateFixedExpenseFromTransaction use (B5
/// batch 7).
/// </summary>
public sealed record MarkTransactionAsPaidCommand(Guid UserId, Guid Id, Guid BudgetPeriodId, Money PaidAmount, DateOnly PaidDate)
    : IRequest<TransactionDto>;

public sealed class MarkTransactionAsPaidCommandHandler(
    BudgetAccessGuard access, ITransactionRepository transactions, IFixedExpenseRepository fixedExpenses,
    IBudgetProfileRepository profiles, ILogger<MarkTransactionAsPaidCommandHandler> logger)
    : IRequestHandler<MarkTransactionAsPaidCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(MarkTransactionAsPaidCommand request, CancellationToken ct)
    {
        var period = await access.EnsureCollaboratorOfPeriodAsync(request.BudgetPeriodId, request.UserId, ct);

        var autoSync = await FixedExpensePaymentSync.AutoUpdatePlannedAmountForAsync(profiles, period.BudgetProfileId, ct);
        var tx = await FixedExpensePaymentSync.MarkPaidAsync(
            transactions, fixedExpenses, request.Id, request.BudgetPeriodId, request.PaidAmount.ToDecimal(), request.PaidDate,
            autoSync, logger, ct);

        return TransactionMapping.ToDto(tx);
    }
}
