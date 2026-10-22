namespace WellSpent.Application.Status;

public sealed record StatusBannerDto(
    Guid Id,
    string Severity,
    IReadOnlyDictionary<string, string> Message,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime CreatedAt);
