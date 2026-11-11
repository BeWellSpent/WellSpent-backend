using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.ExpenseSummary;

public sealed class SpendFilterTests
{
    [Theory]
    [InlineData(TransactionFlow.Spend, false)]
    [InlineData(TransactionFlow.Refund, false)]
    [InlineData(TransactionFlow.Transfer, false)] // transfers count, signed: a Zelle to the landlord is rent
    [InlineData(TransactionFlow.Income, true)]
    [InlineData(TransactionFlow.Payment, true)]
    public void IsNonSpendTransaction_ByFlow(string flow, bool nonSpend) =>
        Assert.Equal(nonSpend, SpendFilter.IsNonSpendTransaction(new Transaction { Amount = 10m, Flow = flow }));

    [Fact]
    public void IsNonSpendTransaction_ExcludedFlag_WinsOverFlow() =>
        Assert.True(SpendFilter.IsNonSpendTransaction(new Transaction { Amount = 10m, Flow = TransactionFlow.Spend, IsExcluded = true }));

    [Fact]
    public void IsNonSpendTransaction_UnsavedRowWithNoFlow_IsSpend() =>
        Assert.False(SpendFilter.IsNonSpendTransaction(new Transaction { Amount = 10m }));
}
