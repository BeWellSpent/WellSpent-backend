using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.PaymentMethods.DeletePaymentMethod;

/// <summary>
/// Mirrors a real gap in Go, carried over rather than silently closed: the
/// final deactivation (`is_active = FALSE`) is not itself scoped by
/// BudgetProfileId, only the reassignment CTEs are — so this only checks
/// that the caller is a collaborator on BudgetProfileId and that both ids
/// exist, not that they actually belong to that profile.
/// </summary>
public sealed record DeletePaymentMethodCommand(Guid UserId, Guid Id, Guid ReplacementId, Guid BudgetProfileId) : IRequest;

public sealed class DeletePaymentMethodCommandHandler(
    ITransactionRepository transactions, BudgetAccessGuard access)
    : IRequestHandler<DeletePaymentMethodCommand>
{
    public async Task Handle(DeletePaymentMethodCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);
        await transactions.GetPaymentMethodAsync(request.Id, ct);
        await transactions.GetPaymentMethodAsync(request.ReplacementId, ct);
        await transactions.DeletePaymentMethodAndReassignAsync(request.Id, request.ReplacementId, request.BudgetProfileId, ct);
    }
}
