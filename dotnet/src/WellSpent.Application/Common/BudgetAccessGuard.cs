using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Common;

/// <summary>
/// Shared "is this caller allowed to touch this budget" checks, used by both
/// the Notification domain (UpsertAlertSubscription) and the Invite domain
/// (Send/List/Cancel). Mirrors two genuinely different Go behaviors — don't
/// unify them, they fail differently on purpose:
///
/// - EnsureMemberAsync (UpsertAlertSubscription's check): a non-member's
///   NotFoundException propagates as-is — a 404, not a 403.
/// - EnsureAdminAsync (Invite's checks): both "not a member" and "member but
///   not admin" collapse to the same ForbiddenException — a 403 either way,
///   same message, so it can't be used to probe which case applied.
/// </summary>
public sealed class BudgetAccessGuard(IBudgetProfileRepository profiles)
{
    public async Task<BudgetProfile> EnsureMemberAsync(Guid profileId, Guid callerId, CancellationToken ct)
    {
        var profile = await profiles.GetByIdAsync(profileId, ct);
        if (profile.UserId != callerId)
        {
            await profiles.GetPersonByUserIdAsync(profileId, callerId, ct);
        }
        return profile;
    }

    public async Task<BudgetProfile> EnsureAdminAsync(Guid profileId, Guid callerId, CancellationToken ct)
    {
        var profile = await profiles.GetByIdAsync(profileId, ct);
        if (profile.UserId != callerId)
        {
            BudgetPerson person;
            try
            {
                person = await profiles.GetPersonByUserIdAsync(profileId, callerId, ct);
            }
            catch (NotFoundException)
            {
                throw new ForbiddenException("only admins can do this");
            }
            if (person.Role != "admin")
            {
                throw new ForbiddenException("only admins can do this");
            }
        }
        return profile;
    }

    /// <summary>Mirrors Go's assertCollaboratorOrAbove — same 404-vs-403 collapse as EnsureAdminAsync, just a wider role set (admin or collaborator).</summary>
    public async Task<BudgetProfile> EnsureCollaboratorOrAboveAsync(Guid profileId, Guid callerId, CancellationToken ct)
    {
        var profile = await profiles.GetByIdAsync(profileId, ct);
        if (profile.UserId != callerId)
        {
            BudgetPerson person;
            try
            {
                person = await profiles.GetPersonByUserIdAsync(profileId, callerId, ct);
            }
            catch (NotFoundException)
            {
                throw new ForbiddenException("access denied");
            }
            if (person.Role != "admin" && person.Role != "collaborator")
            {
                throw new ForbiddenException("access denied");
            }
        }
        return profile;
    }
}
