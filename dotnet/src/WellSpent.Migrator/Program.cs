using DbUp;
using DbUp.Engine;
using DotNetEnv;
using WellSpent.Infrastructure;

var cmd = args.Length > 0 ? args[0] : "up";
var env = args.Length > 1 ? args[1] : "dev";

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);

// Mirrors cmd/migrate/main.go: loads .env.<env> from the repo root, non-fatal
// if absent since prod injects DATABASE_URL directly (Cloud Run secrets).
var envFile = Path.Combine(repoRoot, $".env.{env}");
if (File.Exists(envFile))
{
    Env.Load(envFile);
}

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("DATABASE_URL is required");
    return 1;
}

if (cmd != "up")
{
    // DbUp is forward-only by design (no down-migration tracking equivalent
    // to goose's). Rollback during the transition still goes through the Go
    // runner (`make migrate-down`), which keeps owning goose's own
    // goose_db_version bookkeeping. Reconciling that bookkeeping with DbUp's
    // own SchemaVersions table — so the two runners can safely interleave
    // against the same database — is explicit follow-up work (sub-issue B7),
    // not solved here.
    Console.Error.WriteLine($"'{cmd}' is not supported — this runner only applies migrations forward (use 'up'). " +
        "For rollback during the transition, use the Go runner: make migrate-down ENV=" + env);
    return 1;
}

// Same schema source of truth as the Go backend — this runner reads the
// existing goose-managed directory directly rather than owning a copy.
var migrationsDir = Path.Combine(repoRoot, "internal", "db", "migrations");
if (!Directory.Exists(migrationsDir))
{
    Console.Error.WriteLine($"migrations directory not found: {migrationsDir}");
    return 1;
}

var scripts = Directory.GetFiles(migrationsDir, "*.sql")
    .OrderBy(path => path, StringComparer.Ordinal)
    .Select(path => new SqlScript(Path.GetFileName(path), ExtractGooseUpSection(File.ReadAllText(path))))
    .ToList();

var npgsqlConnectionString = PostgresConnectionString.FromDatabaseUrl(connectionString).ConnectionString;

var upgrader = DeployChanges.To
    .PostgresqlDatabase(npgsqlConnectionString)
    .WithScripts(scripts)
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    Console.Error.WriteLine(result.Error);
    return 1;
}

Console.WriteLine($"Applied {scripts.Count} migration(s) successfully.");
return 0;

// Each goose migration file holds both an "-- +goose Up" and an
// "-- +goose Down" section in the same file. DbUp has no concept of that
// convention and runs a script's entire contents verbatim — handing it the
// raw file would execute the Down section (which DROPs everything the Up
// section just created) immediately after the Up section, in the same run.
// Only the text between the Up marker and the Down marker (or end of file,
// if there's no Down section) is a valid DbUp script.
static string ExtractGooseUpSection(string sql)
{
    const string upMarker = "-- +goose Up";
    const string downMarker = "-- +goose Down";

    var upIndex = sql.IndexOf(upMarker, StringComparison.Ordinal);
    var start = upIndex >= 0 ? upIndex + upMarker.Length : 0;

    var downIndex = sql.IndexOf(downMarker, start, StringComparison.Ordinal);
    var end = downIndex >= 0 ? downIndex : sql.Length;

    return sql[start..end];
}

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "go.mod")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName
        ?? throw new InvalidOperationException("Could not locate WellSpent-backend repo root (go.mod not found)");
}
