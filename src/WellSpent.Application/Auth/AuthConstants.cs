namespace WellSpent.Application.Auth;

/// <summary>Mirrors internal/service/auth_service.go's constants exactly.</summary>
public static class AuthConstants
{
    /// <summary>JWT lifetime for Login (remember_me=false), Register, and ExchangeGoogleCode — every auth flow except Login's remember-me path and the OAuth flows (which have no "remember me" UI, see RememberMeTokenLifetime).</summary>
    public static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromHours(24);

    /// <summary>Login's remember_me=true path, plus both OAuth flows (Google and Apple), which always issue this since neither has a "remember me" control.</summary>
    public static readonly TimeSpan RememberMeTokenLifetime = TimeSpan.FromDays(90);

    /// <summary>Throttles ResendVerificationEmail so a user (or attacker) can't trigger unlimited emails to an address.</summary>
    public static readonly TimeSpan VerificationResendCooldown = TimeSpan.FromSeconds(60);

    /// <summary>oauth_account.oauth_name values — the table's UNIQUE (oauth_name, account_id) already namespaces providers.</summary>
    public const string OauthProviderGoogle = "google";
    public const string OauthProviderApple = "apple";
}
