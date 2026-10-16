using MediatR;
using WellSpent.Application.Common;
using WellSpent.Application.Transactions;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.FixedExpenses.CreateInstallmentPlan;

public sealed record CreateInstallmentPlanCommand(
    Guid UserId, Guid TransactionId, Guid BudgetPeriodId, DateOnly FirstPaymentDate, int TotalPayments, DateOnly? EndDate)
    : IRequest<CreateInstallmentPlanResult>;

public sealed record CreateInstallmentPlanResult(FixedExpenseDto Expense, TransactionDto Transaction);

/// <summary>
/// Mirrors Go's CreateInstallmentPlan exactly, including its two most
/// deliberate decisions: scoped by profile (EnsureCollaboratorOrAboveAsync),
/// not by period, so it's explicitly allowed on an archived period — realising
/// later that a purchase was financed is the normal case; and the plan is
/// created before the transaction is excluded/linked, not transactionally —
/// a failure between the two leaves a visible plan next to a still-counting
/// purchase, which the user can see and resolve, rather than a silently
/// excluded transaction with nothing to explain it.
/// </summary>
public sealed class CreateInstallmentPlanCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions, IFixedExpenseRepository fixedExpenses)
    : IRequestHandler<CreateInstallmentPlanCommand, CreateInstallmentPlanResult>
{
    public async Task<CreateInstallmentPlanResult> Handle(CreateInstallmentPlanCommand request, CancellationToken ct)
    {
        if (request.TotalPayments < 2)
        {
            throw new AppValidationException("an installment plan needs at least 2 payments");
        }

        var period = await profiles.GetPeriodByIdAsync(request.BudgetPeriodId, ct);
        await access.EnsureCollaboratorOrAboveAsync(period.BudgetProfileId, request.UserId, ct);

        var tx = await transactions.GetTransactionAsync(request.TransactionId, ct);
        if (tx.BudgetPeriodId != request.BudgetPeriodId)
        {
            throw new NotFoundException("transaction", request.TransactionId.ToString());
        }
        if (tx.InstallmentFixedExpenseId is not null)
        {
            throw new AppValidationException("this transaction is already an installment plan");
        }
        if (tx.TransactionTypeId == 1)
        {
            throw new AppValidationException("only a variable transaction can be split into installments");
        }
        if (tx.Amount <= 0)
        {
            // A received amount is stored negative — splitting money that came in across future payments is not a thing.
            throw new AppValidationException("only a spend can be split into installments");
        }

        var endDate = request.EndDate ?? InstallmentMath.InstallmentEndDate(request.FirstPaymentDate, request.TotalPayments);
        if (endDate < request.FirstPaymentDate)
        {
            throw new AppValidationException("the plan cannot end before its first payment");
        }

        var fields = new FixedExpenseFields(
            tx.Name ?? "", Money.FromDecimal(InstallmentMath.InstallmentAmount(tx.Amount, request.TotalPayments)),
            tx.CategoryId, tx.PaymentMethodId, 0, 1, request.FirstPaymentDate, "month", 1, 1, endDate, request.TotalPayments);

        var (fe, _) = await FixedExpenseCreation.CreateAndSpawnAsync(
            fixedExpenses, profiles, transactions, period.BudgetProfileId, fields, ct, isInstallmentPlan: true);

        var updated = await transactions.SetInstallmentPlanAsync(request.TransactionId, request.BudgetPeriodId, fe.Id, ct);

        return new CreateInstallmentPlanResult(FixedExpenseMapping.ToDto(fe, DateOnly.FromDateTime(DateTime.UtcNow)), TransactionMapping.ToDto(updated));
    }
}
