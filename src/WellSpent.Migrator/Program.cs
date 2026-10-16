using DbUp;
using DbUp.Engine;
using DotNetEnv;
using WellSpent.Infrastructure;
using WellSpent.Migrator;

var cmd = args.Length > 0 ? args[0] : "up";
var env = args.Length > 1 ? args[1] : "dev";

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);

// Loads .env.<env> from the repo root, non-fatal if absent since prod
// injects DATABASE_URL directly (Cloud Run secrets).
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
    // DbUp is forward-only by design — there is no down-migration tracking
    // equivalent to goose's. A rollback has to be written as a new forward
    // migration (or applied by hand against the database).
    Console.Error.WriteLine($"'{cmd}' is not supported — this runner only applies migrations forward (use 'up'). " +
        "There is no automated rollback; write a new forward migration or apply the reversal by hand.");
    return 1;
}

// Same schema source of truth the Go backend used to read before it was
// decommissioned — goose's own naming/format stuck even though goose itself
// is gone, since every existing filename already encodes migration order.
var migrationsDir = Path.Combine(repoRoot, "db", "migrations");
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

// GooseCompatibleJournal reads/writes goose's own goose_db_version table
// instead of DbUp's default SchemaVersions, so this runner and the Go
// backend's cmd/migrate can safely interleave against the same database.
var upgrader = DeployChanges.To
    .PostgresqlDatabase(npgsqlConnectionString)
    .WithScripts(scripts)
    .JournalTo(new GooseCompatibleJournal(npgsqlConnectionString, scripts, Console.WriteLine))
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
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WellSpent.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName
        ?? throw new InvalidOperationException("Could not locate WellSpent-backend repo root (WellSpent.slnx not found)");
}
