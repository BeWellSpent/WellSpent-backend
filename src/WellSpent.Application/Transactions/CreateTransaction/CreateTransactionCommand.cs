using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.Notifications;
using WellSpent.Application.TransactionReviews;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Transactions.CreateTransaction;

/// <summary>
/// Mirrors Go's Create: a newly created Variable transaction (with a period)
/// is scored against active fixed expenses, queuing a review at >=80, and
/// other subscribed budget members are notified of the new transaction
/// (both via NotificationDispatcher).
/// </summary>
public sealed record CreateTransactionCommand(
    Guid UserId, string? Name, Money Amount, Money PlannedAmount, DateOnly? Date, DateOnly? RenewalDate,
    Guid? BudgetPeriodId, int? CategoryId, Guid? PaymentMethodId, string? TransactionFrequency, string? TransactionType)
    : IRequest<TransactionDto>;

public sealed class CreateTransactionCommandHandler(
    BudgetAccessGuard access, ITransactionRepository transactions, IBudgetProfileRepository profiles,
    IFixedExpenseRepository fixedExpenses, ITransactionReviewRepository reviews, IUserRepository users,
    NotificationDispatcher notifications, ILogger<CreateTransactionCommandHandler> logger)
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
                created, createdPeriodId, request.UserId, profiles, fixedExpenses, reviews, users, notifications, logger, ct);
            await notifications.HandleNewTransactionAsync(created, createdPeriodId, request.UserId, ct);
        }

        return TransactionMapping.ToDto(created);
    }
}
