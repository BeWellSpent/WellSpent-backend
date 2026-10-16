using WellSpent.Application.Transactions;
using Xunit;

namespace WellSpent.Application.Tests.Transactions;

public sealed class TransactionFrequencyMappingTests
{
    [Theory]
    [InlineData(1, "one_off")]
    [InlineData(2, "weekly")]
    [InlineData(3, "bi_weekly")]
    [InlineData(4, "monthly")]
    [InlineData(5, "yearly")]
    [InlineData(0, "unspecified")]
    [InlineData(null, "unspecified")]
    public void ToName_MatchesSeededOrder(int? id, string expected) =>
        Assert.Equal(expected, TransactionFrequencyMapping.ToName(id));

    [Theory]
    [InlineData("monthly", 4)]
    [InlineData("bogus", null)]
    [InlineData(null, null)]
    public void ToId_RoundTrips(string? name, int? expected) =>
        Assert.Equal(expected, TransactionFrequencyMapping.ToId(name));
}

public sealed class TransactionTypeMappingTests
{
    [Theory]
    [InlineData(1, "fixed")]
    [InlineData(2, "variable")]
    [InlineData(0, "unspecified")]
    [InlineData(null, "unspecified")]
    public void ToName_MatchesSeededOrder(int? id, string expected) =>
        Assert.Equal(expected, TransactionTypeMapping.ToName(id));

    [Theory]
    [InlineData("fixed", 1)]
    [InlineData("variable", 2)]
    [InlineData("bogus", null)]
    public void ToId_RoundTrips(string name, int? expected) =>
        Assert.Equal(expected, TransactionTypeMapping.ToId(name));
}
