using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.TransactionReviews;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Transactions.CreateTransaction;

/// <summary>
/// Mirrors Go's Create. Completes batch 4's own deferred manual-match HOOK
/// now that FixedExpense and TransactionReview both exist: a newly created
/// Variable transaction (with a period) is scored against active fixed
/// expenses, queuing a review at >=80 — best-effort, per Go's own posture.
/// One side effect remains deferred: notifying other subscribed budget
/// members of a new transaction needs the Notification domain's internal
/// dispatch wiring, not yet done for any domain.
/// </summary>
public sealed record CreateTransactionCommand(
    Guid UserId, string? Name, Money Amount, Money PlannedAmount, DateOnly? Date, DateOnly? RenewalDate,
    Guid? BudgetPeriodId, int? CategoryId, Guid? PaymentMethodId, string? TransactionFrequency, string? TransactionType)
    : IRequest<TransactionDto>;

public sealed class CreateTransactionCommandHandler(
    BudgetAccessGuard access, ITransactionRepository transactions, IBudgetProfileRepository profiles,
    IFixedExpenseRepository fixedExpenses, ITransactionReviewRepository reviews, IUserRepository users,
    ILogger<CreateTransactionCommandHandler> logger)
    : IRequestHandler<CreateTransactionCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(CreateTransactionCommand request, CancellationToken ct)
    {
        var transactionTypeId = TransactionTypeMapping.ToId(request.TransactionType);

        if (request.BudgetPeriodId is { } periodId)
        {
            var period = await access.EnsureCollaboratorOfPeriodAsync(periodId, request.UserId, ct);
            TransactionRules.AssertNotBackdated(transactionTypeId, request.Date, period);
        }

        var created = await transactions.CreateTransactionAsync(new Transaction
        {
            Name = request.Name,
            Amount = request.Amount.ToDecimal(),
            PlannedAmount = request.PlannedAmount.ToDecimal(),
            Date = request.Date,
            RenewalDate = request.RenewalDate,
            BudgetPeriodId = request.BudgetPeriodId,
            CategoryId = request.CategoryId,
            PaymentMethodId = request.PaymentMethodId,
            TransactionFrequencyId = TransactionFrequencyMapping.ToId(request.TransactionFrequency),
            TransactionTypeId = transactionTypeId,
        }, ct);

        if (transactionTypeId == 2 && request.BudgetPeriodId is { } createdPeriodId)
        {
            await ManualMatchReview.MaybeQueueReviewAsync(
                created, createdPeriodId, request.UserId, profiles, fixedExpenses, reviews, users, logger, ct);
        }
        // HOOK: notify other subscribed budget members of a new transaction.

        return TransactionMapping.ToDto(created);
    }
}
