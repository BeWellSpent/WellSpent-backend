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

    /// <summary>
    /// Mirrors TransactionService's own assertProfileMember/getUserRoleForProfile
    /// — a non-member always gets ForbiddenException here, never
    /// NotFoundException. This is the TransactionService-flavored member
    /// check (used by ListTransactionReviews); it is a genuinely different
    /// behavior from EnsureMemberAsync above (BudgetProfileService's own
    /// assertMember, which lets a non-member's NotFoundException propagate
    /// as a 404) — both exist in Go on different services and neither is
    /// "the bug", so neither is unified here.
    /// </summary>
    public async Task<BudgetProfile> EnsureMemberForbiddenAsync(Guid profileId, Guid callerId, CancellationToken ct)
    {
        var profile = await profiles.GetByIdAsync(profileId, ct);
        if (profile.UserId != callerId)
        {
            try
            {
                await profiles.GetPersonByUserIdAsync(profileId, callerId, ct);
            }
            catch (NotFoundException)
            {
                throw new ForbiddenException("access denied");
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

    /// <summary>
    /// Mirrors TransactionService's own getUserRoleForPeriod: resolves the
    /// period's profile and the caller's role. A non-member always gets
    /// ForbiddenException here, never NotFoundException — unlike
    /// EnsureMemberAsync above, which lets a non-member's NotFoundException
    /// propagate. Go has both behaviors on different services; this is the
    /// TransactionService one specifically.
    /// </summary>
    public async Task<(BudgetPeriod Period, string Role)> GetPeriodRoleAsync(Guid periodId, Guid callerId, CancellationToken ct)
    {
        var period = await profiles.GetPeriodByIdAsync(periodId, ct);
        var profile = await profiles.GetByIdAsync(period.BudgetProfileId, ct);
        if (profile.UserId == callerId)
        {
            return (period, "admin");
        }
        try
        {
            var person = await profiles.GetPersonByUserIdAsync(period.BudgetProfileId, callerId, ct);
            return (period, person.Role);
        }
        catch (NotFoundException)
        {
            throw new ForbiddenException("access denied");
        }
    }

    public async Task<BudgetPeriod> EnsureMemberOfPeriodAsync(Guid periodId, Guid callerId, CancellationToken ct)
    {
        var (period, _) = await GetPeriodRoleAsync(periodId, callerId, ct);
        return period;
    }

    /// <summary>
    /// Mirrors TransactionService's own assertPeriodCollaborator: the role
    /// check runs BEFORE the archived check. This is the opposite order from
    /// the income-entry path (BudgetProfileService's assertPeriodCollaborator,
    /// archived-first) — a genuine inconsistency in Go between the two
    /// services, not something to "fix" into one shared order here.
    /// </summary>
    public async Task<BudgetPeriod> EnsureCollaboratorOfPeriodAsync(Guid periodId, Guid callerId, CancellationToken ct)
    {
        var (period, role) = await GetPeriodRoleAsync(periodId, callerId, ct);
        if (role != "admin" && role != "collaborator")
        {
            throw new ForbiddenException("access denied");
        }
        if (period.IsArchived)
        {
            throw new ForbiddenException("this budget period is archived and read-only");
        }
        return period;
    }
}
