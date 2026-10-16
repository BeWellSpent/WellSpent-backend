using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.TransactionReviews;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.TransactionReviews.ConfirmTransactionReview;

/// <summary>
/// Access is member-only (EnsureMemberOfPeriodAsync), not collaborator-or-
/// above — mirrors a real Go permissiveness quirk: Go's ConfirmTransactionReview
/// resolves the caller's role via getUserRoleForPeriod but discards it,
/// checking only that the caller is a member. A Viewer can confirm a review.
/// Mirrored as-is, not tightened here. The request's own budgetProfileId is
/// also unused by Go beyond request shape — the profile actually acted on is
/// always the one resolved from the review's real period, never what the
/// caller claims.
/// </summary>
public sealed record ConfirmTransactionReviewCommand(Guid UserId, Guid ReviewId) : IRequest;

public sealed class ConfirmTransactionReviewCommandHandler(
    BudgetAccessGuard access, ITransactionReviewRepository reviews, ITransactionRepository transactions,
    IFixedExpenseRepository fixedExpenses, IBudgetProfileRepository profiles, ILogger<ConfirmTransactionReviewCommandHandler> logger)
    : IRequestHandler<ConfirmTransactionReviewCommand>
{
    public async Task Handle(ConfirmTransactionReviewCommand request, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(request.ReviewId, ct);
        var period = await access.EnsureMemberOfPeriodAsync(review.BudgetPeriodId, request.UserId, ct);

        await TransactionReviewConfirmation.ConfirmTransactionMatchAsync(
            transactions, fixedExpenses, reviews, profiles, review, period.BudgetProfileId, logger, ct);
    }
}
