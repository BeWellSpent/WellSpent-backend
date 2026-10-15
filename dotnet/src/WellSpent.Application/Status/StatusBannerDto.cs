namespace WellSpent.Application.Status;

public sealed record StatusBannerDto(
    Guid Id,
    string Severity,
    string MessageEn,
    string MessageEs,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime CreatedAt);
