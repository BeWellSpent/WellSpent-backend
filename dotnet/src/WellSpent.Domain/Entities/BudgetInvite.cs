namespace WellSpent.Domain.Entities;

/// <summary>Maps 1:1 to the existing `budget_invite` table — a single-use, time-limited invitation to join a budget.</summary>
public sealed class BudgetInvite
{
    public Guid Id { get; set; }
    public Guid BudgetProfileId { get; set; }
    public required string Email { get; set; }

    /// <summary>"admin" | "collaborator" | "viewer" — admin is rejected at the Application layer; the setup flow only ever offers collaborator/viewer.</summary>
    public required string Role { get; set; }

    public Guid Token { get; set; }

    /// <summary>"pending" | "accepted" | "cancelled" | "expired".</summary>
    public required string Status { get; set; }

    public Guid InvitedBy { get; set; }

    /// <summary>
    /// Non-null when linked to an existing placeholder BudgetPerson rather
    /// than creating a new one on acceptance. The column is BIGINT even
    /// though BudgetPerson.Id (the FK target) is a 32-bit SERIAL — an
    /// existing inconsistency in the schema, mirrored as-is rather than
    /// "fixed", so values always fit in int32 despite the wider column.
    /// </summary>
    public long? BudgetPersonId { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
