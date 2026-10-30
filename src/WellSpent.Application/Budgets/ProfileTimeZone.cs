namespace WellSpent.Application.Budgets;

/// <summary>Per-country timezone gate for cycle-budgets, so a profile never cycles before its own local midnight. US gates on the westmost US zone (Pacific) — the latest zone to reach any given date — so no US timezone is ever cycled early.</summary>
public static class ProfileTimeZone
{
    private static readonly Dictionary<string, string> ZoneIdByCountry = new()
    {
        ["US"] = "America/Los_Angeles",
        ["AR"] = "America/Argentina/Buenos_Aires",
        ["ES"] = "Europe/Madrid",
    };

    private static readonly HashSet<string> EmeaCountries = ["ES"];

    public static TimeZoneInfo Resolve(string? countryCode) =>
        TimeZoneInfo.FindSystemTimeZoneById(
            countryCode is not null && ZoneIdByCountry.TryGetValue(countryCode, out var id) ? id : "Etc/UTC");

    /// <summary>True once it's actually the day after endDate in this profile's own timezone — i.e. cycling now would not be early.</summary>
    public static bool IsPastLocalMidnightAfter(DateOnly endDate, string? countryCode, DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Resolve(countryCode));
        return DateOnly.FromDateTime(local) > endDate;
    }

    /// <summary>
    /// "emea" matches only EmeaCountries; any other region value (including "americas")
    /// matches the complement — deliberately, so a future country nobody's added to
    /// EmeaCountries still gets evaluated by *some* scheduled invocation instead of
    /// silently never cycling.
    /// </summary>
    public static bool MatchesRegion(string? countryCode, string? region) =>
        region is null || (region == "emea" ? EmeaCountries.Contains(countryCode!) : !EmeaCountries.Contains(countryCode!));
}
