using MediatR;
using WellSpent.Application.Common;
using WellSpent.Application.Transactions;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.FixedExpenses.CreateFixedExpense;

public sealed record CreateFixedExpenseCommand(Guid UserId, Guid BudgetProfileId, FixedExpenseFields Fields)
    : IRequest<CreateFixedExpenseResult>;

public sealed record CreateFixedExpenseResult(FixedExpenseDto Expense, TransactionDto? Transaction);

/// <summary>Mirrors Go's CreateFixedExpense — see FixedExpenseCreation for the create-then-spawn logic shared with CreateInstallmentPlan.</summary>
public sealed class CreateFixedExpenseCommandHandler(
    BudgetAccessGuard access, IFixedExpenseRepository fixedExpenses, IBudgetProfileRepository profiles, ITransactionRepository transactions)
    : IRequestHandler<CreateFixedExpenseCommand, CreateFixedExpenseResult>
{
    public async Task<CreateFixedExpenseResult> Handle(CreateFixedExpenseCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var (fe, tx) = await FixedExpenseCreation.CreateAndSpawnAsync(
            fixedExpenses, profiles, transactions, request.BudgetProfileId, request.Fields, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new CreateFixedExpenseResult(
            FixedExpenseMapping.ToDto(fe, today), tx is null ? null : TransactionMapping.ToDto(tx));
    }
}
