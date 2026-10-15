using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Invites;

/// <summary>
/// Shared by GetBudgetInviteQuery and AcceptBudgetInviteCommand — Go's
/// Accept calls its own GetByToken internally, reusing the exact same
/// cancelled/accepted/expired validation rather than duplicating it.
/// </summary>
public static class InviteLookup
{
    public static async Task<BudgetInvite> GetValidAsync(
        IInviteRepository invites, ILogger logger, Guid token, CancellationToken ct)
    {
        var invite = await invites.GetByTokenAsync(token, ct);

        if (invite.Status == "cancelled")
        {
            throw new AppValidationException("this invitation has been cancelled");
        }
        if (invite.Status == "accepted")
        {
            throw new AppValidationException("this invitation has already been accepted");
        }
        if (invite.ExpiresAt < DateTime.UtcNow)
        {
            try
            {
                await invites.UpdateStatusAsync(invite.Id, "expired", ct);
            }
            catch (Exception ex)
            {
                // The invite stays listed as pending though it can no longer be accepted.
                logger.LogError(ex, "invite.mark_expired_failed id={InviteId}", invite.Id);
            }
            throw new AppValidationException("this invitation has expired");
        }

        return invite;
    }
}
