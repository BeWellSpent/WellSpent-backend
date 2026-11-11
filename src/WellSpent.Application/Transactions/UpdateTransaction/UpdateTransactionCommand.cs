using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.Notifications;
using WellSpent.Application.TransactionReviews;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Transactions.UpdateTransaction;

/// <summary>
/// A full edit, except for a Plaid-imported row, where only CategoryId and Flow may change.
/// once the period has archived, and for any Plaid-imported row at any time.
/// Deliberately does NOT call EnsureCollaboratorOfPeriodAsync (which would
/// hard-block an archived period outright) — it does the role check directly
/// so the archived case can still go through the category-only path. If the
/// transaction has no BudgetPeriodId at all, Go skips every access check
/// entirely; mirrored as-is, not treated as a bug to close here.
///
/// Completes batch 4's own deferred manual-match HOOK (B5 batch 7): editing
/// a Variable transaction re-scores it against active fixed expenses, which
/// can both queue a new review and remove a now-stale pending one.
/// </summary>
public sealed record UpdateTransactionCommand(
    Guid UserId, Guid Id, string? Name, Money Amount, Money PlannedAmount, DateOnly? Date,
    int? CategoryId, Guid? PaymentMethodId, string? TransactionFrequency, string? TransactionType, string? Flow = null)
    : IRequest<TransactionDto>;

public sealed class UpdateTransactionCommandHandler(
    BudgetAccessGuard access, ITransactionRepository transactions, IBudgetProfileRepository profiles,
    IFixedExpenseRepository fixedExpenses, ITransactionReviewRepository reviews, IUserRepository users,
    NotificationDispatcher notifications, ILogger<UpdateTransactionCommandHandler> logger)
    : IRequestHandler<UpdateTransactionCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(UpdateTransactionCommand request, CancellationToken ct)
    {
        var existing = await transactions.GetTransactionAsync(request.Id, ct);
        var transactionTypeId = TransactionTypeMapping.ToId(request.TransactionType);
        TransactionRules.AssertValidFlow(request.Flow);

        var edit = new Transaction
        {
            Id = request.Id,
            Name = request.Name,
            Amount = request.Amount.ToDecimal(),
            PlannedAmount = request.PlannedAmount.ToDecimal(),
            Date = request.Date,
            CategoryId = TransactionRules.CategoryOrNull(request.CategoryId),
            PaymentMethodId = request.PaymentMethodId,
            TransactionFrequencyId = TransactionFrequencyMapping.ToId(request.TransactionFrequency),
            TransactionTypeId = transactionTypeId,
            Flow = request.Flow, // null: re-derived on save if the category or sign changed
        };

        if (existing.BudgetPeriodId is { } periodId)
        {
            var (period, role) = await access.GetPeriodRoleAsync(periodId, request.UserId, ct);
            if (role != "admin" && role != "collaborator")
            {
                throw new ForbiddenException("access denied");
            }
            // A bank-imported row keeps what the bank said; only its category and flow are the user's.
            if (existing.PlaidTransactionId is not null)
            {
                TransactionRules.AssertOnlyCategoryChanged(edit, existing);
            }
        }

        var updated = await transactions.UpdateTransactionAsync(edit, ct);

        if (transactionTypeId == 2 && updated.BudgetPeriodId is { } updatedPeriodId)
        {
            await ManualMatchReview.MaybeQueueReviewAsync(
                updated, updatedPeriodId, request.UserId, profiles, fixedExpenses, reviews, users, notifications, logger, ct);
        }

        return TransactionMapping.ToDto(updated);
    }
}
