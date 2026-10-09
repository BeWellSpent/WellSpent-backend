using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses.UpdateFixedExpense;

public sealed record UpdateFixedExpenseCommand(Guid UserId, Guid Id, Guid BudgetProfileId, FixedExpenseFields Fields)
    : IRequest<FixedExpenseDto>;

/// <summary>
/// Mirrors Go's UpdateFixedExpense: after the template itself is replaced,
/// reconciles the active period's transaction — spawns one if the edit made
/// the expense newly due, deletes the unpaid one if it's no longer due, or
/// propagates the edit onto the existing transaction (a narrower set of
/// fields if it's already paid). WEEK-unit expenses skip reconciliation
/// entirely: a period can hold several of their occurrences, so there's no
/// single "the current period's transaction" to reconcile.
///
/// Deferred via HOOK: Go also re-scores any pending TransactionReview
/// against the reconciled transaction — not possible yet (TransactionReview
/// is B5 batch 7). That refresh is itself nil-safe/best-effort in Go, so its
/// absence changes nothing about whether the reconciliation above succeeds.
/// </summary>
public sealed class UpdateFixedExpenseCommandHandler(
    BudgetAccessGuard access, IFixedExpenseRepository fixedExpenses, IBudgetProfileRepository profiles, ITransactionRepository transactions)
    : IRequestHandler<UpdateFixedExpenseCommand, FixedExpenseDto>
{
    public async Task<FixedExpenseDto> Handle(UpdateFixedExpenseCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var entity = FixedExpenseNormalization.BuildEntity(request.Fields);
        entity.Id = request.Id;
        entity.BudgetProfileId = request.BudgetProfileId;
        var fe = await fixedExpenses.UpdateAsync(entity, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (FixedExpenseScheduling.IsWeekUnit(fe))
        {
            return FixedExpenseMapping.ToDto(fe, today);
        }

        BudgetPeriod period;
        try
        {
            period = await profiles.GetLatestPeriodAsync(request.BudgetProfileId, ct);
        }
        catch (NotFoundException)
        {
            return FixedExpenseMapping.ToDto(fe, today);
        }

        var startDate = period.StartDate;

        if (!FixedExpenseScheduling.IsDueInMonth(fe, startDate))
        {
            try
            {
                await fixedExpenses.DeleteUnpaidTransactionsAsync(fe.Id, request.BudgetProfileId, ct);
            }
            catch (Exception)
            {
                // A stale unpaid bill outlives the template change that should have replaced it.
            }
            return FixedExpenseMapping.ToDto(fe, today);
        }

        var txDate = FixedExpenseScheduling.DateInMonth(fe, startDate);

        // Asks whether the bill exists at all, not whether it is unpaid — the
        // narrower question made an already-paid bill look absent and spawned
        // a duplicate (issue #62).
        var existing = await fixedExpenses.GetTransactionAsync(fe.Id, request.BudgetProfileId, ct);

        if (existing is null)
        {
            // No existing transaction — the expense just became due.
            try
            {
                await transactions.CreateTransactionAsync(new Transaction
                {
                    Name = fe.Name,
                    Amount = fe.PlannedAmount,
                    PlannedAmount = fe.PlannedAmount,
                    Date = txDate,
                    BudgetPeriodId = period.Id,
                    CategoryId = fe.CategoryId,
                    PaymentMethodId = fe.PaymentMethodId,
                    TransactionTypeId = 1,
                    FixedExpenseId = fe.Id,
                }, ct);
            }
            catch (Exception)
            {
                // A bill the user owes this period simply never appears.
            }
        }
        else if (existing.IsPaid)
        {
            // Settled: only the descriptive fields follow the template. Amount
            // and the paid flag stay as recorded.
            try
            {
                await fixedExpenses.UpdatePaidTransactionFromFixedExpenseAsync(
                    fe.Id, request.BudgetProfileId, fe.Name, fe.CategoryId, fe.PaymentMethodId, ct);
            }
            catch (Exception)
            {
                // The paid row keeps the old category/payment method while the template shows the new one.
            }
        }
        else
        {
            try
            {
                await fixedExpenses.UpdateTransactionFromFixedExpenseAsync(
                    fe.Id, request.BudgetProfileId, fe.Name, fe.PlannedAmount, fe.CategoryId, fe.PaymentMethodId, txDate, ct);
            }
            catch (Exception)
            {
                // The current period keeps the old amount while the template shows the new one.
            }
        }

        return FixedExpenseMapping.ToDto(fe, today);
    }
}
