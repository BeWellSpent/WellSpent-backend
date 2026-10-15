namespace WellSpent.Application.Changelog;

/// <summary>Mirrors internal/service/changelog_service.go's stored component/change-type values and caps.</summary>
public static class ChangelogConstants
{
    public const string ComponentWeb = "web";
    public const string ComponentIos = "ios";
    public const string ComponentServer = "server";
    public static readonly HashSet<string> ValidComponents = [ComponentWeb, ComponentIos, ComponentServer];

    public const string ChangeTypeAdded = "added";
    public const string ChangeTypeFixed = "fixed";
    public const string ChangeTypeChanged = "changed";
    public static readonly HashSet<string> ValidChangeTypes = [ChangeTypeAdded, ChangeTypeFixed, ChangeTypeChanged];

    public const int MaxSummaryLength = 300;
    public const int MaxVersionLength = 40;
    public const int DefaultReleasesPerComponent = 20;

    /// <summary>
    /// What this build calls itself. Kept equal to Go's internal/version/version.go
    /// Current for as long as both backends are live — clients see one logical
    /// "server" version regardless of which backend answered. Revisit at
    /// decommission (sub-issue D), when only this one remains.
    /// </summary>
    public const string ServerVersion = "1.7.0";
}
