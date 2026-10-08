using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Users;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Invites.AcceptBudgetInvite;

/// <summary>Links the authenticated user to the budget. If the invitee has no account they must register first (handled client-side — this RPC always requires a bearer token).</summary>
public sealed record AcceptBudgetInviteCommand(Guid CallerId, Guid Token) : IRequest<Guid>;

public sealed class AcceptBudgetInviteCommandHandler(
    IInviteRepository invites, IBudgetProfileRepository profiles, IUserRepository users,
    ILogger<AcceptBudgetInviteCommandHandler> logger) : IRequestHandler<AcceptBudgetInviteCommand, Guid>
{
    public async Task<Guid> Handle(AcceptBudgetInviteCommand request, CancellationToken ct)
    {
        var invite = await InviteLookup.GetValidAsync(invites, logger, request.Token, ct);

        // Verify the caller's account still exists — JWT is stateless so the
        // auth middleware doesn't catch a deleted user, and the FK on
        // user_id would otherwise surface as a raw DB error.
        await users.GetByIdAsync(request.CallerId, ct);

        var already = await profiles.ExistsPersonForUserAsync(invite.BudgetProfileId, request.CallerId, ct);
        if (already)
        {
            // Idempotent: mark accepted (best-effort) and return the budget
            // id so the frontend can redirect either way.
            await TryMarkAcceptedAsync(invite.Id, ct);
            return invite.BudgetProfileId;
        }

        if (invite.BudgetPersonId is { } personId)
        {
            // Safe: the column is BIGINT but always holds a value that came
            // from a 32-bit SERIAL PK (see BudgetInvite.BudgetPersonId).
            await profiles.LinkPersonToUserAsync((int)personId, request.CallerId, invite.Role, ct);
        }
        else
        {
            var caller = await users.GetByIdAsync(request.CallerId, ct);
            await profiles.AddPersonAsync(new BudgetPerson
            {
                BudgetProfileId = invite.BudgetProfileId,
                UserName = UserDisplayRules.DisplayName(caller),
                UserId = request.CallerId,
                Color = "",
                Role = invite.Role,
            }, ct);
        }

        await TryMarkAcceptedAsync(invite.Id, ct);
        return invite.BudgetProfileId;
    }

    // The invite stays pending and can be redeemed a second time if this fails.
    private async Task TryMarkAcceptedAsync(Guid inviteId, CancellationToken ct)
    {
        try
        {
            await invites.UpdateStatusAsync(inviteId, "accepted", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "invite.mark_accepted_failed id={InviteId}", inviteId);
        }
    }
}
