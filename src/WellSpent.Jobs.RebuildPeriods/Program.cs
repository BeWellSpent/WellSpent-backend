using Npgsql;
using WellSpent.Application.Budgets;
using WellSpent.Domain.Entities;
using WellSpent.Infrastructure;

// Manual, one-profile ops tool — not deployed (not in the Dockerfile). Recomputes a profile's
// whole period timeline from the income schedule that is in force today, then re-homes
// everything onto it. Prints the plan by default; --apply performs it in ONE transaction.
//
//   DATABASE_URL=... dotnet run --project src/WellSpent.Jobs.RebuildPeriods -- \
//       --profile=<guid> --from=2026-06-01 [--delete-carryover] [--apply]
//   --email=<owner email>  lists that owner's profile ids and exits
//
// Re-homing is by transaction date. Income entries are recreated for every new period from the
// profile's current recurring sources (past amounts edited by hand are not preserved).
var opts = args.Where(a => a.StartsWith("--")).Select(a => a[2..].Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "true");
if (opts.TryGetValue("email", out var email))
{
    var databaseUrlForLookup = Environment.GetEnvironmentVariable("DATABASE_URL") ?? throw new InvalidOperationException("DATABASE_URL is required");
    await using var lookup = new NpgsqlConnection(PostgresConnectionString.FromDatabaseUrl(databaseUrlForLookup).ConnectionString);
    await lookup.OpenAsync();
    await using var find = new NpgsqlCommand("SELECT p.id, p.name, p.cycle, p.created_at FROM budget_profile p JOIN users u ON u.id = p.user_id WHERE lower(u.email) = lower(@e) ORDER BY p.created_at", lookup);
    find.Parameters.AddWithValue("e", email);
    await using var found = await find.ExecuteReaderAsync();
    while (await found.ReadAsync()) Console.WriteLine($"{found.GetGuid(0)}  \"{found.GetString(1)}\"  cycle={found.GetString(2)}  created={found.GetDateTime(3):yyyy-MM-dd}");
    return 0;
}
if (!opts.TryGetValue("profile", out var profileArg) || !Guid.TryParse(profileArg, out var profileId)
    || !opts.TryGetValue("from", out var fromArg) || !DateOnly.TryParse(fromArg, out var from))
{
    Console.Error.WriteLine("usage: --profile=<guid> --from=YYYY-MM-DD [--delete-carryover] [--apply]");
    return 2;
}
var apply = opts.ContainsKey("apply");
var deleteCarryover = opts.ContainsKey("delete-carryover");
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL") ?? throw new InvalidOperationException("DATABASE_URL is required");

var builder = PostgresConnectionString.FromDatabaseUrl(databaseUrl);
builder.ApplicationName = "wellspent-rebuild-periods";
await using var conn = new NpgsqlConnection(builder.ConnectionString);
await conn.OpenAsync();

static DateOnly? AsDate(object? v) => v switch { DateOnly d => d, DateTime t => DateOnly.FromDateTime(t), _ => null };

async Task<List<object?[]>> Rows(string sql, NpgsqlTransaction? tx = null, params (string, object)[] ps)
{
    await using var cmd = new NpgsqlCommand(sql, conn, tx);
    foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
    await using var r = await cmd.ExecuteReaderAsync();
    var list = new List<object?[]>();
    while (await r.ReadAsync())
    {
        var row = new object?[r.FieldCount];
        r.GetValues(row!);
        list.Add(row.Select(v => v is DBNull ? null : v).ToArray());
    }
    return list;
}
async Task<int> Exec(NpgsqlTransaction tx, string sql, params (string, object)[] ps)
{
    await using var cmd = new NpgsqlCommand(sql, conn, tx);
    foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
    return await cmd.ExecuteNonQueryAsync();
}

var profileRow = (await Rows("SELECT name, cycle FROM budget_profile WHERE id = @p", null, ("p", profileId))).SingleOrDefault()
    ?? throw new InvalidOperationException($"profile {profileId} not found");
var cycle = (string)profileRow[1]!;
Console.WriteLine($"profile {profileId} \"{profileRow[0]}\" cycle={cycle} mode={(apply ? "APPLY" : "DRY RUN")}");

var sources = (await Rows(
    "SELECT id, name, payment_frequency, anchor_date, early_arrival_tolerance_days, adjust_for_weekends, effective_from, effective_to, recurring, default_amount, budget_person_id FROM income_source WHERE budget_profile_id = @p ORDER BY id",
    null, ("p", profileId)))
    .Select(r => new IncomeSource
    {
        Id = Convert.ToInt32(r[0]), BudgetProfileId = profileId, Name = (string)r[1]!, PaymentFrequency = (string)r[2]!,
        AnchorDate = AsDate(r[3]),
        EarlyArrivalToleranceDays = Convert.ToInt32(r[4]), AdjustForWeekends = (bool)r[5]!,
        EffectiveFrom = AsDate(r[6]),
        EffectiveTo = AsDate(r[7]),
        Recurring = (bool)r[8]!, DefaultAmount = (decimal)r[9]!, BudgetPersonId = r[10] is null ? null : Convert.ToInt32(r[10]),
    }).ToList();

// The rebuilt timeline is one schedule applied to the whole history: only today's roster counts,
// and its effective dates (set when a schedule edit was queued for "the next period") are ignored.
// --apply also clears them, so periods generated afterwards read the same schedule.
var historical = sources.Count(s => s.EffectiveTo is not null);
sources = sources.Where(s => s.EffectiveTo is null).ToList();
foreach (var s in sources) s.EffectiveFrom = null;
Console.WriteLine($"\nincome sources (current roster; {historical} superseded rows ignored):");
foreach (var s in sources)
    Console.WriteLine($"  id={s.Id} {s.PaymentFrequency,-9} anchor={s.AnchorDate:yyyy-MM-dd} tol={s.EarlyArrivalToleranceDays} weekdayAdj={s.AdjustForWeekends} from={s.EffectiveFrom:yyyy-MM-dd} to={s.EffectiveTo:yyyy-MM-dd} recurring={s.Recurring} amount={s.DefaultAmount} regular={IncomePeriodDates.IsRegular(s)}");

var today = DateOnly.FromDateTime(DateTime.UtcNow);
var plan = new List<(DateOnly Start, DateOnly End)>();
var first = IncomePeriodDates.ComputeFirst(sources, cycle, from);
plan.Add(first);
while (plan[^1].End < today && plan.Count < 60)
    plan.Add(IncomePeriodDates.ComputeNext(sources, cycle, plan[^1].End));

var oldPeriods = (await Rows(
    "SELECT p.id, p.start_date, p.end_date, p.is_archived, p.needs_review, (SELECT count(*) FROM transaction t WHERE t.budget_period_id = p.id), (SELECT count(*) FROM transaction_review r WHERE r.budget_period_id = p.id), (SELECT count(*) FROM income_entry e WHERE e.budget_period_id = p.id) FROM budget_period p WHERE p.budget_profile_id = @p ORDER BY p.start_date",
    null, ("p", profileId))).ToList();
Console.WriteLine("\ncurrent periods:");
foreach (var o in oldPeriods)
    Console.WriteLine($"  {AsDate(o[1]):yyyy-MM-dd} -> {AsDate(o[2]):yyyy-MM-dd} archived={o[3]} review={o[4]} tx={o[5]} reviews={o[6]} incomeEntries={o[7]}");

Console.WriteLine("\nnew periods (today = " + today.ToString("yyyy-MM-dd") + "):");
for (var i = 0; i < plan.Count; i++)
{
    var gap = i == 0 ? "" : $" gapFromPrev={plan[i].Start.DayNumber - plan[i - 1].End.DayNumber}";
    Console.WriteLine($"  {plan[i].Start:ddd yyyy-MM-dd} -> {plan[i].End:ddd yyyy-MM-dd}{gap}{(i >= plan.Count - 2 ? "  (editable)" : "  (archived)")}");
}

var oldIds = oldPeriods.Select(o => (Guid)o[0]!).ToArray();
var txRows = await Rows("SELECT id, date, carried_from_budget_period_id IS NOT NULL, period_overridden FROM transaction WHERE budget_period_id = ANY(@ids)", null, ("ids", oldIds));
int PeriodIndex(DateOnly? d) => d is null ? 0 : d < plan[0].Start ? 0 : d > plan[^1].End ? plan.Count - 1 : plan.FindIndex(p => p.Start <= d && d <= p.End);
var perPeriod = new int[plan.Count];
foreach (var t in txRows) perPeriod[PeriodIndex(AsDate(t[1]))]++;
var outside = txRows.Count(t => AsDate(t[1]) is { } d && (d < plan[0].Start || d > plan[^1].End)) + txRows.Count(t => t[1] is null);
var carried = txRows.Count(t => (bool)t[2]!);
var overridden = txRows.Count(t => (bool)t[3]!);
var recurring = sources.Where(s => s.Recurring && s.EffectiveTo is null).ToList();
Console.WriteLine($"\ntransactions to re-home: {txRows.Count} (outside the new range, clamped to nearest end: {outside}; carried-over rows: {carried} -> {(deleteCarryover ? "DELETED" : "kept")}; manual period overrides: {overridden}, re-homed by date)");
Console.WriteLine("per new period: " + string.Join(", ", plan.Select((p, i) => $"{p.Start:MMM d}={perPeriod[i]}")));
Console.WriteLine($"income entries: {oldPeriods.Sum(o => Convert.ToInt32(o[7]))} existing deleted, {recurring.Count * plan.Count} created ({recurring.Count} recurring sources x {plan.Count} periods)");

if (!apply) { Console.WriteLine("\nDRY RUN: nothing written. Re-run with --apply to perform it."); return 0; }

// Everything --apply is about to overwrite, saved first so a manual undo is possible.
var backupPath = opts.GetValueOrDefault("backup") ?? $"rebuild-periods-backup-{profileId}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
var backup = new Dictionary<string, object>
{
    ["periods"] = await Rows("SELECT * FROM budget_period WHERE budget_profile_id = @p ORDER BY start_date", null, ("p", profileId)),
    ["transactions"] = await Rows("SELECT id, budget_period_id, carried_from_budget_period_id FROM transaction WHERE budget_period_id = ANY(@ids)", null, ("ids", oldIds)),
    ["reviews"] = await Rows("SELECT id, budget_period_id FROM transaction_review WHERE budget_period_id = ANY(@ids)", null, ("ids", oldIds)),
    ["incomeEntries"] = await Rows("SELECT * FROM income_entry WHERE budget_period_id = ANY(@ids)", null, ("ids", oldIds)),
    ["incomeSourceEffectiveFrom"] = await Rows("SELECT id, effective_from FROM income_source WHERE budget_profile_id = @p", null, ("p", profileId)),
};
await File.WriteAllTextAsync(backupPath, System.Text.Json.JsonSerializer.Serialize(backup));
Console.WriteLine($"backup written: {Path.GetFullPath(backupPath)}");
await using var tx = await conn.BeginTransactionAsync();
var newIds = plan.Select(_ => Guid.NewGuid()).ToArray();
for (var i = 0; i < plan.Count; i++)
{
    await Exec(tx, "INSERT INTO budget_period (id, budget_profile_id, start_date, end_date, is_archived, needs_review) VALUES (@id, @p, @s, @e, @a, false)",
        ("id", newIds[i]), ("p", profileId), ("s", plan[i].Start), ("e", plan[i].End), ("a", i < plan.Count - 2));
}
if (deleteCarryover)
{
    var n = await Exec(tx, "DELETE FROM transaction WHERE budget_period_id = ANY(@ids) AND carried_from_budget_period_id IS NOT NULL", ("ids", oldIds));
    Console.WriteLine($"deleted carried-over rows: {n}");
}
for (var i = 0; i < plan.Count; i++)
{
    var lo = i == 0 ? DateOnly.MinValue : plan[i].Start;
    var hi = i == plan.Count - 1 ? DateOnly.MaxValue : plan[i].End;
    await Exec(tx, "UPDATE transaction SET budget_period_id = @n WHERE budget_period_id = ANY(@ids) AND date BETWEEN @lo AND @hi", ("n", newIds[i]), ("ids", oldIds), ("lo", lo), ("hi", hi));
}
await Exec(tx, "UPDATE transaction SET budget_period_id = @n WHERE budget_period_id = ANY(@ids) AND date IS NULL", ("n", newIds[0]), ("ids", oldIds));
// Kept carryover rows point at the period they were carried FROM: the one before wherever they now sit.
for (var i = 1; i < plan.Count; i++)
    await Exec(tx, "UPDATE transaction SET carried_from_budget_period_id = @prev WHERE budget_period_id = @n AND carried_from_budget_period_id IS NOT NULL", ("prev", newIds[i - 1]), ("n", newIds[i]));
await Exec(tx, "UPDATE transaction SET carried_from_budget_period_id = NULL WHERE budget_period_id = @n AND carried_from_budget_period_id IS NOT NULL", ("n", newIds[0]));
await Exec(tx, "UPDATE transaction_review r SET budget_period_id = t.budget_period_id FROM transaction t WHERE r.transaction_id = t.id AND r.budget_period_id = ANY(@ids)", ("ids", oldIds));
var orphanReviews = await Rows("SELECT count(*) FROM transaction_review WHERE budget_period_id = ANY(@ids)", tx, ("ids", oldIds));
if (Convert.ToInt32(orphanReviews[0][0]) > 0) throw new InvalidOperationException($"{orphanReviews[0][0]} reviews still point at old periods; rolling back");
var leftover = await Rows("SELECT count(*) FROM transaction WHERE budget_period_id = ANY(@ids) OR carried_from_budget_period_id = ANY(@ids)", tx, ("ids", oldIds));
if (Convert.ToInt32(leftover[0][0]) > 0) throw new InvalidOperationException($"{leftover[0][0]} transactions still point at old periods; rolling back");
await Exec(tx, "DELETE FROM income_entry WHERE budget_period_id = ANY(@ids)", ("ids", oldIds));
await Exec(tx, "DELETE FROM budget_period WHERE id = ANY(@ids)", ("ids", oldIds));
foreach (var src in recurring)
    for (var i = 0; i < plan.Count; i++)
        await Exec(tx, "INSERT INTO income_entry (budget_period_id, income_source_id, budget_person_id, name, amount) VALUES (@b, @s, @pp, @nm, @am)",
            ("b", newIds[i]), ("s", src.Id), ("pp", (object?)src.BudgetPersonId ?? DBNull.Value), ("nm", src.Name), ("am", src.DefaultAmount));
var cleared = await Exec(tx, "UPDATE income_source SET effective_from = NULL WHERE budget_profile_id = @p AND effective_to IS NULL AND effective_from IS NOT NULL", ("p", profileId));
Console.WriteLine($"cleared effective_from on {cleared} income sources");
await tx.CommitAsync();
Console.WriteLine("APPLIED and committed.");
return 0;
