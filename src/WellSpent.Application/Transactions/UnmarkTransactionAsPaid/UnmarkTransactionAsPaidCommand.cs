using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Transactions.UnmarkTransactionAsPaid;

/// <summary>
/// Undoes every *confirmed* TransactionReview against this transaction —
/// drops the fixed-expense alias, un-excludes the imported variable
/// transaction, and dismisses each such review — not just one. Dismissed,
/// not reset to pending: the user just told us this specific match was
/// wrong (or at least not final), so it must not immediately resurface as
/// a new suggestion in the Review tab — a plain "Dismiss" has the exact
/// same no-other-side-effects status transition, this just also runs the
/// alias/exclusion cleanup a real dismiss doesn't need to. Every step here
/// is best-effort/logged-and-continued, so a failure never un-does the
/// unmark itself.
/// </summary>
public sealed record UnmarkTransactionAsPaidCommand(Guid UserId, Guid Id, Guid BudgetPeriodId) : IRequest<TransactionDto>;

public sealed class UnmarkTransactionAsPaidCommandHandler(
    BudgetAccessGuard access, ITransactionRepository transactions, ITransactionReviewRepository reviews,
    ILogger<UnmarkTransactionAsPaidCommandHandler> logger)
    : IRequestHandler<UnmarkTransactionAsPaidCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(UnmarkTransactionAsPaidCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOfPeriodAsync(request.BudgetPeriodId, request.UserId, ct);
        var tx = await transactions.UnmarkTransactionAsPaidAsync(request.Id, request.BudgetPeriodId, ct);

        List<Domain.Entities.TransactionReview> matchedReviews;
        try
        {
            matchedReviews = await reviews.ListByMatchedTransactionIdAsync(tx.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "transaction.unmark_paid: list reviews for transaction {TransactionId}", tx.Id);
            matchedReviews = [];
        }

        var anyConfirmed = false;
        foreach (var review in matchedReviews)
        {
            if (review.Status != "confirmed") continue;
            anyConfirmed = true;

            if (tx.FixedExpenseId is { } feId)
            {
                try
                {
                    var importedTx = await transactions.GetTransactionAsync(review.TransactionId, ct);
                    if (importedTx.Name is { } name)
                    {
                        await reviews.DeleteAliasAsync(feId, name, ct);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "transaction.unmark_paid: drop alias for fixed expense {FixedExpenseId}", feId);
                }
            }

            try
            {
                await transactions.SetTransactionExcludedAsync(review.TransactionId, review.BudgetPeriodId, false, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "transaction.unmark_paid: un-exclude imported transaction {TransactionId}", review.TransactionId);
            }
        }

        if (anyConfirmed)
        {
            try
            {
                await reviews.DismissConfirmedByMatchedTransactionAsync(tx.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "transaction.unmark_paid: dismiss reviews for transaction {TransactionId}", tx.Id);
            }
        }

        return TransactionMapping.ToDto(tx);
    }
}
