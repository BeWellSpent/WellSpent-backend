using Npgsql;

namespace WellSpent.Infrastructure;

/// <summary>
/// Converts a URI-style DATABASE_URL (postgresql://user:pass@host:port/db?sslmode=require)
/// — the format this project uses everywhere, in .env files and Cloud Run
/// secrets, because Go's pgxpool.ParseConfig accepts it directly — into an
/// Npgsql keyword=value connection string. NpgsqlConnectionStringBuilder does
/// not parse the URI form itself, so every .NET consumer of DATABASE_URL
/// (the API's DbContext, the migrator) must go through this rather than
/// re-deriving the parsing, or the two could silently disagree on a param.
/// </summary>
public static class PostgresConnectionString
{
    public static NpgsqlConnectionStringBuilder FromDatabaseUrl(string databaseUrl)
    {
        var uri = new Uri(databaseUrl);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && userInfo[0].Length > 0)
        {
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        }
        if (userInfo.Length > 1)
        {
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            if (!key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                // Other query params (e.g. Neon's channel_binding) are
                // deliberately ignored rather than erroring — the common
                // case here is just sslmode, and an unsupported param that
                // actually mattered fails loudly enough at connect time.
                continue;
            }

            var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            builder.SslMode = value.ToLowerInvariant() switch
            {
                "disable" => SslMode.Disable,
                "allow" => SslMode.Allow,
                "prefer" => SslMode.Prefer,
                "require" => SslMode.Require,
                "verify-ca" => SslMode.VerifyCA,
                "verify-full" => SslMode.VerifyFull,
                _ => SslMode.Prefer,
            };
        }

        return builder;
    }
}
