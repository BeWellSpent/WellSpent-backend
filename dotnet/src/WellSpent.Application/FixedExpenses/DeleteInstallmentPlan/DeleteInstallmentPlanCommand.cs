using MediatR;
using WellSpent.Application.Common;
using WellSpent.Application.Transactions;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses.DeleteInstallmentPlan;

public sealed record DeleteInstallmentPlanCommand(Guid UserId, Guid TransactionId, Guid BudgetPeriodId) : IRequest<TransactionDto>;

/// <summary>
/// Reverses CreateInstallmentPlan: the plan and every payment it spawned are
/// deleted, and the original purchase counts again. Refused once any payment
/// has been marked paid — real recorded spend, and losing it to an undo is
/// worse than making the user unmark it first (also stops this becoming a
/// way to delete transactions out of an archived period). The link is
/// cleared BEFORE the plan is deleted: a mid-way failure then leaves a
/// counting purchase beside a still-live plan, visible and fixable, rather
/// than an excluded transaction pointing at nothing.
/// </summary>
public sealed class DeleteInstallmentPlanCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions, IFixedExpenseRepository fixedExpenses)
    : IRequestHandler<DeleteInstallmentPlanCommand, TransactionDto>
{
    public async Task<TransactionDto> Handle(DeleteInstallmentPlanCommand request, CancellationToken ct)
    {
        var period = await profiles.GetPeriodByIdAsync(request.BudgetPeriodId, ct);
        await access.EnsureCollaboratorOrAboveAsync(period.BudgetProfileId, request.UserId, ct);

        var tx = await transactions.GetTransactionAsync(request.TransactionId, ct);
        if (tx.InstallmentFixedExpenseId is not { } feId)
        {
            throw new AppValidationException("this transaction is not an installment plan");
        }

        var spawned = await transactions.ListByFixedExpenseAsync(feId, ct);
        if (spawned.Any(p => p.IsPaid))
        {
            throw new AppValidationException("this plan already has a payment marked paid; unmark it before undoing the split");
        }

        var updated = await transactions.ClearInstallmentPlanAsync(request.TransactionId, request.BudgetPeriodId, ct);
        await transactions.DeleteByFixedExpenseAsync(feId, ct);
        // Deactivate, not a hard delete — matches how fixed expenses are removed everywhere else.
        await fixedExpenses.DeactivateAsync(feId, period.BudgetProfileId, ct);

        return TransactionMapping.ToDto(updated);
    }
}
