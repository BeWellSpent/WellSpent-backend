using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Infrastructure.Persistence;

public sealed class TransactionReviewRepository(WellSpentDbContext db) : ITransactionReviewRepository
{
    public async Task<TransactionReview> UpsertAsync(Guid budgetPeriodId, Guid transactionId, Guid matchedTransactionId, decimal score, CancellationToken ct)
    {
        var rows = await db.TransactionReviews.FromSqlInterpolated($"""
            INSERT INTO transaction_review (budget_period_id, transaction_id, matched_transaction_id, match_score, status)
            VALUES ({budgetPeriodId}, {transactionId}, {matchedTransactionId}, {score}, 'pending')
            ON CONFLICT (transaction_id) DO UPDATE SET
                matched_transaction_id = EXCLUDED.matched_transaction_id,
                match_score            = EXCLUDED.match_score,
                status                 = 'pending'
            RETURNING id, budget_period_id, transaction_id, matched_transaction_id, match_score, status, created_at
            """).ToListAsync(ct);
        return rows[0];
    }

    public async Task<List<TransactionReviewListRow>> ListAsync(Guid budgetProfileId, CancellationToken ct)
    {
        var joined = await (
            from tr in db.TransactionReviews
            join t in db.Transactions on tr.TransactionId equals t.Id
            join mt in db.Transactions on tr.MatchedTransactionId equals mt.Id
            join bp in db.BudgetPeriods on tr.BudgetPeriodId equals bp.Id
            where bp.BudgetProfileId == budgetProfileId
                && tr.Status != "dismissed"
                && (tr.Status == "confirmed" || !bp.IsArchived)
            orderby tr.MatchScore descending
            select new { tr, t, mt }
        ).ToListAsync(ct);

        var methodIds = joined
            .SelectMany(x => new[] { x.t.PaymentMethodId, x.mt.PaymentMethodId })
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var personByMethod = await db.PaymentMethods
            .Where(pm => methodIds.Contains(pm.Id))
            .ToDictionaryAsync(pm => pm.Id, pm => pm.BudgetPersonId, ct);

        int? PersonFor(Guid? paymentMethodId) =>
            paymentMethodId is { } id && personByMethod.TryGetValue(id, out var personId) ? personId : null;

        return joined.Select(x => new TransactionReviewListRow(
            x.tr.Id, x.tr.BudgetPeriodId, x.tr.TransactionId, x.tr.MatchedTransactionId,
            x.tr.MatchScore, x.tr.Status, x.tr.CreatedAt,
            x.t.Name, x.t.Amount, x.mt.Name,
            PersonFor(x.t.PaymentMethodId), PersonFor(x.mt.PaymentMethodId))).ToList();
    }

    public async Task<TransactionReview> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.TransactionReviews.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("transaction_review", id.ToString());

    public async Task UpdateStatusAsync(Guid id, string status, CancellationToken ct) =>
        await db.TransactionReviews.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status), ct);

    public async Task<TransactionReview?> GetByTransactionIdAsync(Guid transactionId, CancellationToken ct) =>
        await db.TransactionReviews.FirstOrDefaultAsync(r => r.TransactionId == transactionId, ct);

    public async Task<List<TransactionReview>> ListByMatchedTransactionIdAsync(Guid matchedTransactionId, CancellationToken ct) =>
        await db.TransactionReviews.Where(r => r.MatchedTransactionId == matchedTransactionId).ToListAsync(ct);

    public async Task DeleteIfPendingAsync(Guid id, CancellationToken ct) =>
        await db.TransactionReviews.Where(r => r.Id == id && r.Status == "pending").ExecuteDeleteAsync(ct);

    public async Task UpdateScoreIfPendingAsync(Guid id, decimal score, CancellationToken ct) =>
        await db.TransactionReviews.Where(r => r.Id == id && r.Status == "pending")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.MatchScore, score), ct);

    public async Task ResetConfirmedByMatchedTransactionAsync(Guid matchedTransactionId, CancellationToken ct) =>
        await db.TransactionReviews.Where(r => r.MatchedTransactionId == matchedTransactionId && r.Status == "confirmed")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "pending"), ct);

    public async Task CreateAliasAsync(Guid fixedExpenseId, string alias, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO fixed_expense_alias (fixed_expense_id, alias)
            VALUES ({fixedExpenseId}, {alias})
            ON CONFLICT (fixed_expense_id, alias) DO NOTHING
            """, ct);

    public async Task DeleteAliasAsync(Guid fixedExpenseId, string alias, CancellationToken ct) =>
        await db.FixedExpenseAliases.Where(a => a.FixedExpenseId == fixedExpenseId && a.Alias == alias).ExecuteDeleteAsync(ct);

    public async Task<List<string>> ListAliasesAsync(Guid fixedExpenseId, CancellationToken ct) =>
        await db.FixedExpenseAliases.Where(a => a.FixedExpenseId == fixedExpenseId).Select(a => a.Alias).ToListAsync(ct);
}
