using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.TransactionReviews.ListTransactionReviews;

public sealed record ListTransactionReviewsQuery(Guid UserId, Guid BudgetProfileId) : IRequest<List<TransactionReviewDto>>;

/// <summary>Mirrors Go's TransactionService.ListTransactionReviews — member-only, Forbidden-collapsing (never 404), matching TransactionService's own assertProfileMember rather than BudgetProfileService's.</summary>
public sealed class ListTransactionReviewsQueryHandler(BudgetAccessGuard access, ITransactionReviewRepository reviews)
    : IRequestHandler<ListTransactionReviewsQuery, List<TransactionReviewDto>>
{
    public async Task<List<TransactionReviewDto>> Handle(ListTransactionReviewsQuery request, CancellationToken ct)
    {
        await access.EnsureMemberForbiddenAsync(request.BudgetProfileId, request.UserId, ct);
        var rows = await reviews.ListAsync(request.BudgetProfileId, ct);
        return rows.Select(TransactionReviewMapping.ToDto).ToList();
    }
}
