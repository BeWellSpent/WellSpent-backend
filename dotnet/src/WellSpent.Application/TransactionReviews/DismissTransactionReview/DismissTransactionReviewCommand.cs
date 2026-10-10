using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.TransactionReviews.DismissTransactionReview;

public sealed record DismissTransactionReviewCommand(Guid UserId, Guid ReviewId) : IRequest;

/// <summary>Member-only — any role, matching Go's assertPeriodMember here (unlike ConfirmTransactionReview, which resolves the role via a different call but likewise never requires collaborator-or-above).</summary>
public sealed class DismissTransactionReviewCommandHandler(BudgetAccessGuard access, ITransactionReviewRepository reviews)
    : IRequestHandler<DismissTransactionReviewCommand>
{
    public async Task Handle(DismissTransactionReviewCommand request, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(request.ReviewId, ct);
        await access.EnsureMemberOfPeriodAsync(review.BudgetPeriodId, request.UserId, ct);
        await reviews.UpdateStatusAsync(review.Id, "dismissed", ct);
    }
}
