using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Common;
using WellSpent.Application.FixedExpenses;
using WellSpent.Application.Transactions;
using WellSpent.Application.TransactionReviews;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses.CreateFixedExpenseFromTransaction;

public sealed record CreateFixedExpenseFromTransactionCommand(
    Guid UserId, Guid TransactionId, Guid BudgetPeriodId, string Name,
    DateOnly? AnchorDate, string? FrequencyUnit, int IntervalMonths, int IntervalWeeks, int DayOfWeek)
    : IRequest<CreateFixedExpenseFromTransactionResult>;

public sealed record CreateFixedExpenseFromTransactionResult(FixedExpenseDto Expense, TransactionDto Transaction);

/// <summary>
/// Deferred from B5 batch 5 (Installment plans + Fixed expenses) because it
/// drives the TransactionReview confirm flow end to end — that flow is the
/// RPC's whole purpose, not a best-effort side effect of it. Mirrors Go's
/// BudgetProfileService.CreateFixedExpenseFromTransaction, with one
/// deliberate simplification: Go calls its own CreateFixedExpense (which
/// re-runs the collaborator-or-above check it already just ran), where this
/// calls FixedExpenseCreation.CreateAndSpawnAsync directly — the shared
/// create-then-spawn helper CreateFixedExpense/CreateInstallmentPlan already
/// use, with this RPC's own access check run exactly once.
/// </summary>
public sealed class CreateFixedExpenseFromTransactionCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions,
    IFixedExpenseRepository fixedExpenses, ITransactionReviewRepository reviews, ILogger<CreateFixedExpenseFromTransactionCommandHandler> logger)
    : IRequestHandler<CreateFixedExpenseFromTransactionCommand, CreateFixedExpenseFromTransactionResult>
{
    public async Task<CreateFixedExpenseFromTransactionResult> Handle(CreateFixedExpenseFromTransactionCommand request, CancellationToken ct)
    {
        var period = await profiles.GetPeriodByIdAsync(request.BudgetPeriodId, ct);
        await access.EnsureCollaboratorOrAboveAsync(period.BudgetProfileId, request.UserId, ct);
        if (period.IsArchived)
        {
            throw new AppValidationException("this budget period is archived and read-only");
        }

        var tx = await transactions.GetTransactionAsync(request.TransactionId, ct);
        if (tx.BudgetPeriodId != request.BudgetPeriodId)
        {
            throw new NotFoundException("transaction", request.TransactionId.ToString());
        }
        if (tx.TransactionTypeId == 1)
        {
            throw new AppValidationException("only a variable transaction can become a fixed expense");
        }
        if (tx.Amount <= 0)
        {
            throw new AppValidationException("only a spend can become a fixed expense");
        }

        var existingReview = await reviews.GetByTransactionIdAsync(request.TransactionId, ct);
        if (existingReview is not null && existingReview.Status != "dismissed")
        {
            throw new AppValidationException("this transaction is already matched to a fixed expense");
        }

        var name = string.IsNullOrEmpty(request.Name) ? tx.Name ?? "" : request.Name;
        var fields = new FixedExpenseFields(
            name, Money.FromDecimal(tx.Amount), tx.CategoryId, tx.PaymentMethodId,
            0, request.IntervalMonths, request.AnchorDate, request.FrequencyUnit,
            request.IntervalWeeks, request.DayOfWeek, null, 0);

        var (fe, spawned) = await FixedExpenseCreation.CreateAndSpawnAsync(
            fixedExpenses, profiles, transactions, period.BudgetProfileId, fields, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // No transaction spawned this period (e.g. a future anchor date) — nothing to match against yet. The source transaction is left as-is.
        if (spawned is null)
        {
            return new CreateFixedExpenseFromTransactionResult(FixedExpenseMapping.ToDto(fe, today), TransactionMapping.ToDto(tx));
        }

        var review = await reviews.UpsertAsync(request.BudgetPeriodId, request.TransactionId, spawned.Id, 100.0m, ct);
        await TransactionReviewConfirmation.ConfirmTransactionMatchAsync(
            transactions, fixedExpenses, reviews, profiles, review, period.BudgetProfileId, logger, ct);

        var updated = await transactions.GetTransactionAsync(request.TransactionId, ct);
        return new CreateFixedExpenseFromTransactionResult(FixedExpenseMapping.ToDto(fe, today), TransactionMapping.ToDto(updated));
    }
}
