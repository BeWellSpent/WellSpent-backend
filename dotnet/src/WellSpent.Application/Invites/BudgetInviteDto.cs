namespace WellSpent.Application.Invites;

public sealed record BudgetInviteDto(
    Guid Id,
    Guid BudgetProfileId,
    string BudgetName,
    string InviterName,
    string Email,
    string Role,
    string Status,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    long? BudgetPersonId);
