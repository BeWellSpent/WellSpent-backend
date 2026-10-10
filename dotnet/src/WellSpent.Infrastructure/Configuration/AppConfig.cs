namespace WellSpent.Infrastructure.Configuration;

/// <summary>
/// Mirrors internal/config/config.go field-for-field (scoped to what Auth/User
/// need so far — later domains extend this the same way the Go Config struct
/// grew per-feature). Loads ENV (default "dev"), then .env.&lt;ENV&gt; from the
/// repo root if present (non-fatal — prod injects vars directly via Cloud Run
/// secrets), same as Go's godotenv.Load.
/// </summary>
public sealed class AppConfig
{
    public required string DatabaseUrl { get; init; }
    public required string JwtSecret { get; init; }
    public required string Env { get; init; }

    public string GoogleClientId { get; init; } = "";
    public string GoogleClientSecret { get; init; } = "";
    public string GoogleRedirectUri { get; init; } = "";

    public string AppleClientId { get; init; } = "com.bewellspent.WellSpent";
    public string AppleTeamId { get; init; } = "76FQ7V92H5";
    public string AppleKeyId { get; init; } = "";
    public string ApplePrivateKey { get; init; } = "";

    public string EncryptionKey { get; init; } = "";

    public string ResendApiKey { get; init; } = "";
    public string ResendFromEmail { get; init; } = "WellSpent <noreply@wellspent.app>";
    public string FrontendUrl { get; init; } = "http://localhost:3000";

    public string TurnstileSecretKey { get; init; } = "";
    public bool CaptchaEnforcementEnabled { get; init; }

    public string PlaidClientId { get; init; } = "";
    public string PlaidSecret { get; init; } = "";
    public string PlaidEnv { get; init; } = "sandbox";
    public int PlaidHttpMaxRetries { get; init; } = 3;
    // Same env var as Go's PLAID_HTTP_RETRY_DELAY, but plain seconds here, not a Go duration string like "5s".
    public TimeSpan PlaidHttpRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public bool PlaidLogRedactSensitive { get; init; } = true;

    /// <summary>Sent as Postgres' application_name — distinct per deployed process.</summary>
    public string ApplicationName => $"wellspent-api-{Env}";

    public static AppConfig Load()
    {
        var env = Environment.GetEnvironmentVariable("ENV") ?? "dev";

        var envFile = Path.Combine(RepoRoot.Find(), $".env.{env}");
        if (File.Exists(envFile))
        {
            DotNetEnv.Env.Load(envFile);
        }

        return new AppConfig
        {
            DatabaseUrl = RequireEnv("DATABASE_URL"),
            JwtSecret = RequireEnv("JWT_SECRET"),
            Env = env,

            GoogleClientId = OptionalEnv("GOOGLE_CLIENT_ID", ""),
            GoogleClientSecret = OptionalEnv("GOOGLE_CLIENT_SECRET", ""),
            GoogleRedirectUri = OptionalEnv("GOOGLE_REDIRECT_URI", ""),

            AppleClientId = OptionalEnv("APPLE_CLIENT_ID", "com.bewellspent.WellSpent"),
            AppleTeamId = OptionalEnv("APPLE_TEAM_ID", "76FQ7V92H5"),
            AppleKeyId = OptionalEnv("APPLE_KEY_ID", ""),
            ApplePrivateKey = NormalizePemKey(OptionalEnv("APPLE_PRIVATE_KEY", "")),

            EncryptionKey = OptionalEnv("ENCRYPTION_KEY", ""),

            ResendApiKey = OptionalEnv("RESEND_API_KEY", ""),
            ResendFromEmail = OptionalEnv("RESEND_FROM_EMAIL", "WellSpent <noreply@wellspent.app>"),
            FrontendUrl = OptionalEnv("FRONTEND_URL", "http://localhost:3000"),

            TurnstileSecretKey = OptionalEnv("TURNSTILE_SECRET_KEY", ""),
            CaptchaEnforcementEnabled = OptionalEnv("CAPTCHA_ENFORCEMENT_ENABLED", "false") == "true",

            PlaidClientId = OptionalEnv("PLAID_CLIENT_ID", ""),
            PlaidSecret = OptionalEnv("PLAID_SECRET", ""),
            PlaidEnv = OptionalEnv("PLAID_ENV", "sandbox"),
            PlaidHttpMaxRetries = int.Parse(OptionalEnv("PLAID_HTTP_MAX_RETRIES", "3")),
            PlaidHttpRetryDelay = TimeSpan.FromSeconds(int.Parse(OptionalEnv("PLAID_HTTP_RETRY_DELAY", "5"))),
            PlaidLogRedactSensitive = OptionalEnv("PLAID_LOG_REDACT_SENSITIVE", "true") == "true",
        };
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required");

    private static string OptionalEnv(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) ?? fallback;

    // Converts a literal `\n` two-character sequence (as found in a raw Cloud
    // Run env var) into a real newline, so a PEM parser can decode it — mirrors
    // config.NormalizePEMKey exactly. A value already containing real
    // newlines (local dev via DotNetEnv) passes through unchanged.
    private static string NormalizePemKey(string key) => key.Replace("\\n", "\n");
}

/// <summary>
/// Locates the WellSpent-backend repo root (identified by go.mod) by walking
/// up from the running assembly's directory — the dotnet/ solution is nested
/// a few levels under it, and its .env.&lt;env&gt; files are the same ones the Go
/// binaries already read, not a second copy.
/// </summary>
internal static class RepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "go.mod")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate WellSpent-backend repo root (go.mod not found)");
    }
}
