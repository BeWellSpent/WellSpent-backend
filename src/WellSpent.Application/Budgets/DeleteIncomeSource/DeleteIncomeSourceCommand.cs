using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.DeleteIncomeSource;

public sealed record DeleteIncomeSourceCommand(Guid UserId, int Id, Guid BudgetProfileId) : IRequest;

public sealed class DeleteIncomeSourceCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, TaxReserveRecalculator taxReserve)
    : IRequestHandler<DeleteIncomeSourceCommand>
{
    public async Task Handle(DeleteIncomeSourceCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);
        await profiles.DeleteIncomeSourceAsync(request.Id, request.BudgetProfileId, ct);
        await taxReserve.RecalculateAsync(request.BudgetProfileId, ct);
    }
}
