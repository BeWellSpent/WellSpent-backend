using WellSpent.Domain.Entities;

namespace WellSpent.Application.Invites;

public static class InviteDisplay
{
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    public static BudgetInviteDto ToDto(BudgetInvite invite, string budgetName, string inviterName) => new(
        invite.Id, invite.BudgetProfileId, budgetName, inviterName, invite.Email,
        invite.Role, invite.Status, invite.ExpiresAt, invite.CreatedAt, invite.BudgetPersonId);

    /// <summary>
    /// Mirrors the Go SQL's COALESCE(first_name || ' ' || last_name, email)
    /// exactly: Postgres string concatenation with a NULL operand yields
    /// NULL, so this falls back to the inviter's email when EITHER name part
    /// is null — not just when both are.
    /// </summary>
    public static string InviterName(User inviter) =>
        inviter.FirstName is not null && inviter.LastName is not null
            ? $"{inviter.FirstName} {inviter.LastName}"
            : inviter.Email;
}
