namespace WellSpent.Api.Configuration;

/// <summary>
/// Mirrors internal/config/config.go's Load(): reads ENV (default "dev"),
/// loads .env.&lt;ENV&gt; from the repo root if present (non-fatal — prod injects
/// vars directly via Cloud Run secrets), then requires DATABASE_URL and
/// JWT_SECRET. Only the two scaffold needs; later sub-issues extend this as
/// each domain needs its own env vars, same growth pattern as the Go config.
/// </summary>
public sealed class AppConfig
{
    public required string DatabaseUrl { get; init; }
    public required string JwtSecret { get; init; }
    public required string Env { get; init; }

    /// <summary>
    /// Sent as Postgres' application_name — must be distinct per deployed
    /// process (see WellSpent.Infrastructure.ServiceCollectionExtensions).
    /// </summary>
    public string ApplicationName => $"wellspent-api-{Env}";

    public static AppConfig Load()
    {
        var env = Environment.GetEnvironmentVariable("ENV") ?? "dev";

        var envFile = Path.Combine(RepoRoot.Find(), $".env.{env}");
        if (File.Exists(envFile))
        {
            DotNetEnv.Env.Load(envFile);
        }

        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new InvalidOperationException("DATABASE_URL is required");
        var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? throw new InvalidOperationException("JWT_SECRET is required");

        return new AppConfig
        {
            DatabaseUrl = databaseUrl,
            JwtSecret = jwtSecret,
            Env = env,
        };
    }
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
