namespace WellSpent.Application.Configuration;

/// <summary>
/// The slice of Infrastructure's AppConfig that Application-layer handlers
/// need, exposed via <c>IOptions&lt;AuthOptions&gt;</c> so Application never
/// depends on Infrastructure's config-loading mechanism directly — populated
/// from AppConfig in the Api composition root (Program.cs).
/// </summary>
public sealed class AuthOptions
{
    public string FrontendUrl { get; set; } = "";
    public string ResendFromEmail { get; set; } = "";
    public bool CaptchaEnforcementEnabled { get; set; }
    public string EncryptionKey { get; set; } = "";
}
