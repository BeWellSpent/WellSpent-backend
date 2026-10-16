namespace WellSpent.Application.Status;

/// <summary>Mirrors internal/service/status_banner_service.go's stored severity values and caps.</summary>
public static class StatusBannerConstants
{
    public const string SeverityInfo = "info";
    public const string SeverityWarning = "warning";
    public const string SeverityCritical = "critical";

    public static readonly HashSet<string> ValidSeverities = [SeverityInfo, SeverityWarning, SeverityCritical];

    /// <summary>Per-language cap from the feature spec; the DB enforces the same limit — this gives the caller a clear validation error instead of a constraint violation surfacing as a 500.</summary>
    public const int MaxMessageLength = 300;

    public const int DefaultListLimit = 50;
}
