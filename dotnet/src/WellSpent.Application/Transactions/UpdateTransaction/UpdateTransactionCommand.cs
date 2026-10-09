using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Transactions.UpdateTransaction;

/// <summary>
/// A full edit, with two exceptions where only CategoryId may still change:
/// once the period has archived, and for any Plaid-imported row at any time.
/// Deliberately does NOT call EnsureCollaboratorOfPeriodAsync (which would
/// hard-block an archived period outright) — it does the role check directly
/// so the archived case can still go through the category-only path. If the
/// transaction has no BudgetPeriodId at all, Go skips every access check
/// entirely; mirrored as-is, not treated as a bug to close here.
/// </summary>
public sealed record UpdateTransactionCommand(
    Guid UserId, Guid Id, string? Name, Money Amount, Money PlannedAmount, DateOnly? Date,
    int? CategoryId, Guid? PaymentMethodId, string? TransactionFrequency, string? TransactionType)
    : IRequest<TransactionDto>;

public sealed class UpdateTransactionCommandHandler(BudgetAccessGuard access, ITransactionRepository transactions)
    : IRequestHandler<UpdateTransactionCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(UpdateTransactionCommand request, CancellationToken ct)
    {
        var existing = await transactions.GetTransactionAsync(request.Id, ct);
        var transactionTypeId = TransactionTypeMapping.ToId(request.TransactionType);

        var edit = new Transaction
        {
            Id = request.Id,
            Name = request.Name,
            Amount = request.Amount.ToDecimal(),
            PlannedAmount = request.PlannedAmount.ToDecimal(),
            Date = request.Date,
            CategoryId = request.CategoryId,
            PaymentMethodId = request.PaymentMethodId,
            TransactionFrequencyId = TransactionFrequencyMapping.ToId(request.TransactionFrequency),
            TransactionTypeId = transactionTypeId,
        };

        if (existing.BudgetPeriodId is { } periodId)
        {
            var (period, role) = await access.GetPeriodRoleAsync(periodId, request.UserId, ct);
            if (role != "admin" && role != "collaborator")
            {
                throw new ForbiddenException("access denied");
            }
            if (period.IsArchived || existing.PlaidTransactionId is not null)
            {
                TransactionRules.AssertOnlyCategoryChanged(edit, existing);
            }
            else
            {
                TransactionRules.AssertNotBackdated(transactionTypeId, request.Date, period);
            }
        }

        var updated = await transactions.UpdateTransactionAsync(edit, ct);

        // HOOK: maybeQueueReview — Plaid-match re-scoring (B5 batches 5/7).

        return TransactionMapping.ToDto(updated);
    }
}
