using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.TransactionReviews.MarkTransactionForReview;

public sealed record MarkTransactionForReviewCommand(Guid UserId, Guid TransactionId, Guid MatchedTransactionId, Guid BudgetProfileId)
    : IRequest<TransactionReviewDto>;

/// <summary>
/// Flags a variable transaction as a likely duplicate of matchedTransactionId
/// — any Fixed-type transaction in the same period, whether spawned from a
/// FixedExpense template or a SavingsSource. Matching against the
/// transaction directly (rather than a FixedExpense template) means any
/// Fixed-type transaction can be a match target with no separate
/// savings-specific path. Access is checked against the caller-supplied
/// budgetProfileId first; only afterward do the subsequent checks verify the
/// transaction/matched transaction actually belong to it.
/// </summary>
public sealed class MarkTransactionForReviewCommandHandler(BudgetAccessGuard access, ITransactionRepository transactions, IBudgetProfileRepository profiles, ITransactionReviewRepository reviews)
    : IRequestHandler<MarkTransactionForReviewCommand, TransactionReviewDto>
{
    public async Task<TransactionReviewDto> Handle(MarkTransactionForReviewCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var tx = await transactions.GetTransactionAsync(request.TransactionId, ct);
        if (tx.TransactionTypeId != 2)
        {
            throw new AppValidationException("only variable transactions can be flagged for review");
        }
        if (tx.BudgetPeriodId is not { } periodId)
        {
            throw new AppValidationException("transaction has no budget period");
        }
        var period = await profiles.GetPeriodByIdAsync(periodId, ct);
        if (period.BudgetProfileId != request.BudgetProfileId)
        {
            throw new ForbiddenException("transaction belongs to a different budget");
        }

        var matched = await transactions.GetTransactionAsync(request.MatchedTransactionId, ct);
        if (matched.TransactionTypeId != 1)
        {
            throw new AppValidationException("can only match against a fixed transaction");
        }
        if (matched.BudgetPeriodId != periodId)
        {
            throw new ForbiddenException("matched transaction belongs to a different budget");
        }

        var review = await reviews.UpsertAsync(periodId, request.TransactionId, request.MatchedTransactionId, 100.0m, ct);

        // HOOK: notify budget members of a pending review (Notification domain's event-dispatch wiring isn't done yet).

        return TransactionReviewMapping.ToDto(review);
    }
}
