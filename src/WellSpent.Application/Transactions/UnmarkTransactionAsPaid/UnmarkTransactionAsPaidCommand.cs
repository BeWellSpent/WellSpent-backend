using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Transactions.UnmarkTransactionAsPaid;

/// <summary>
/// Mirrors Go's UnmarkTransactionAsPaid, now completing its own deferred
/// HOOK (B5 batch 7): undoes every *confirmed* TransactionReview against
/// this transaction — drops the fixed-expense alias, un-excludes the
/// imported variable transaction, and resets each such review to pending —
/// not just one. Every step here is best-effort/logged-and-continued in Go,
/// so a failure never un-does the unmark itself.
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
                await reviews.ResetConfirmedByMatchedTransactionAsync(tx.Id, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "transaction.unmark_paid: reset reviews for transaction {TransactionId}", tx.Id);
            }
        }

        return TransactionMapping.ToDto(tx);
    }
}
