namespace WellSpent.Application.Notifications;

public sealed record NotificationDto(
    Guid Id, Guid UserId, Guid? BudgetProfileId, string AlertType, string Title, string? Body, bool IsRead, DateTime CreatedAt);

public sealed record AlertSubscriptionDto(
    Guid Id, Guid UserId, Guid BudgetProfileId, string AlertType, string Channel,
    decimal? ThresholdPct, string? ThresholdScope, int? CategoryId, bool NotifyAllMembers, DateTime CreatedAt);
