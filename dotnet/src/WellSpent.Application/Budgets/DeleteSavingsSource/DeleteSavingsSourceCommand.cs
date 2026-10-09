using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.DeleteSavingsSource;

/// <summary>Deferred: deleting the source's auto-created transactions (B5 batch 4/5) — the row itself deletes fully today.</summary>
public sealed record DeleteSavingsSourceCommand(Guid UserId, int Id, Guid BudgetProfileId) : IRequest;

public sealed class DeleteSavingsSourceCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles)
    : IRequestHandler<DeleteSavingsSourceCommand>
{
    public async Task Handle(DeleteSavingsSourceCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        // Throws NotFoundException if missing — Go reads it only to find the
        // payment method for the deferred transaction-cleanup HOOK below.
        await profiles.GetSavingsSourceAsync(request.Id, request.BudgetProfileId, ct);

        // HOOK: delete the source's auto-created transactions (B5 batch 4/5).

        await profiles.DeleteSavingsSourceAsync(request.Id, request.BudgetProfileId, ct);
    }
}
