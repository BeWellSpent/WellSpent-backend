using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Invites.CancelBudgetInvite;

public sealed record CancelBudgetInviteCommand(Guid CallerId, Guid InviteId) : IRequest<Unit>;

public sealed class CancelBudgetInviteCommandHandler(IInviteRepository invites, BudgetAccessGuard access)
    : IRequestHandler<CancelBudgetInviteCommand, Unit>
{
    public async Task<Unit> Handle(CancelBudgetInviteCommand request, CancellationToken ct)
    {
        var invite = await invites.GetByIdAsync(request.InviteId, ct);
        await access.EnsureAdminAsync(invite.BudgetProfileId, request.CallerId, ct);

        if (invite.Status != "pending")
        {
            throw new AppValidationException("only pending invites can be cancelled");
        }

        await invites.UpdateStatusAsync(request.InviteId, "cancelled", ct);
        return Unit.Value;
    }
}
