using WellSpent.Application.Budgets;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class ProfileTimeZoneTests
{
    // Nov 1 00:00 UTC is still Oct 31 in America/Los_Angeles (UTC-7 in DST)
    // but already Nov 1 in Europe/Madrid (UTC+1).
    private static readonly DateTime UtcInstant = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsPastLocalMidnightAfter_US_StillPreviousDayLocally_ReturnsFalse()
    {
        Assert.False(ProfileTimeZone.IsPastLocalMidnightAfter(new DateOnly(2026, 10, 31), "US", UtcInstant));
    }

    [Fact]
    public void IsPastLocalMidnightAfter_ES_AlreadyPastLocally_ReturnsTrue()
    {
        Assert.True(ProfileTimeZone.IsPastLocalMidnightAfter(new DateOnly(2026, 10, 31), "ES", UtcInstant));
    }

    [Fact]
    public void IsPastLocalMidnightAfter_UnmappedCountry_FallsBackToUtc()
    {
        Assert.True(ProfileTimeZone.IsPastLocalMidnightAfter(new DateOnly(2026, 10, 31), "ZZ", UtcInstant));
    }

    [Theory]
    [InlineData(null, "US", true)]
    [InlineData(null, "ES", true)]
    [InlineData("emea", "ES", true)]
    [InlineData("emea", "US", false)]
    [InlineData("americas", "US", true)]
    [InlineData("americas", "ES", false)]
    [InlineData("americas", "ZZ", true)] // unmapped country matches the complement, never silently excluded
    public void MatchesRegion_TruthTable(string? region, string? countryCode, bool expected)
    {
        Assert.Equal(expected, ProfileTimeZone.MatchesRegion(countryCode, region));
    }
}
