namespace WellSpent.Application.AppAttest;

/// <summary>Populated from AppConfig in Program.cs, same as AuthOptions.</summary>
public sealed class AppAttestOptions
{
    /// <summary>Off: everything is verified and logged, nothing is blocked.</summary>
    public bool EnforcementEnabled { get; set; }

    /// <summary>Team ID + bundle ID.</summary>
    public string AppId { get; set; } = "";

    /// <summary>Accept keys from Apple's development environment (Xcode builds); off in prod.</summary>
    public bool AllowDevelopment { get; set; }
}
