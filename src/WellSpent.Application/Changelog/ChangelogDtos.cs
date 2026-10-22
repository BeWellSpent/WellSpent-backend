namespace WellSpent.Application.Changelog;

public sealed record ChangelogItemDto(string ChangeType, IReadOnlyDictionary<string, string> Summary);

public sealed record ChangelogReleaseDto(
    Guid Id, string Component, string Version, DateTime ReleasedAt, List<ChangelogItemDto> Items, DateTime CreatedAt);

public sealed record ChangelogListResult(List<ChangelogReleaseDto> Releases, string CurrentServerVersion);
