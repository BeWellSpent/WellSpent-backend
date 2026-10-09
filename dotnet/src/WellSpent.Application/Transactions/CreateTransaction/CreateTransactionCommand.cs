using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Transactions.CreateTransaction;

/// <summary>
/// Mirrors Go's Create, with two side effects deferred via HOOK comments
/// below rather than attempted now: queuing a Plaid-match review (needs
/// FixedExpense — B5 batch 5 — and TransactionReview — B5 batch 7) and
/// notifying other subscribed budget members of a new transaction (needs the
/// Notification domain's internal dispatch, not yet wired to this trigger).
/// Both are explicitly best-effort/non-fatal in Go, so their absence changes
/// nothing about whether this transaction itself gets created correctly.
/// </summary>
public sealed record CreateTransactionCommand(
    Guid UserId, string? Name, Money Amount, Money PlannedAmount, DateOnly? Date, DateOnly? RenewalDate,
    Guid? BudgetPeriodId, int? CategoryId, Guid? PaymentMethodId, string? TransactionFrequency, string? TransactionType)
    : IRequest<TransactionDto>;

public sealed class CreateTransactionCommandHandler(BudgetAccessGuard access, ITransactionRepository transactions)
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

        // HOOK: maybeQueueReview — Plaid-match scoring against fixed expenses (B5 batches 5/7).
        // HOOK: notify other subscribed budget members of a new transaction.

        return TransactionMapping.ToDto(created);
    }
}
