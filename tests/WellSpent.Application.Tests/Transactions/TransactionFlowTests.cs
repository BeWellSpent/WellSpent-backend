using WellSpent.Application.Transactions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Transactions;

public sealed class TransactionFlowTests
{
    [Theory]
    [InlineData(10, TransactionFlow.Spend)]
    [InlineData(0, TransactionFlow.Spend)]
    [InlineData(-10, TransactionFlow.Refund)]
    public void BySign(int amount, string expected) => Assert.Equal(expected, TransactionFlow.BySign(amount));

    [Theory]
    [InlineData(TransactionFlow.Spend, true)]
    [InlineData(TransactionFlow.Refund, true)]
    [InlineData(TransactionFlow.Transfer, true)]
    [InlineData(TransactionFlow.Income, false)]
    [InlineData(TransactionFlow.Payment, false)]
    public void CountsAsSpending(string flow, bool counts) => Assert.Equal(counts, TransactionFlow.CountsAsSpending(flow));

    [Fact]
    public void OnAmountChange_OnlySpendAndRefundFollowTheSign()
    {
        Assert.Equal(TransactionFlow.Refund, TransactionFlow.OnAmountChange(TransactionFlow.Spend, -1m));
        Assert.Equal(TransactionFlow.Transfer, TransactionFlow.OnAmountChange(TransactionFlow.Transfer, -1m));
    }

    [Fact]
    public void AssertValidFlow_RejectsUnknownValues()
    {
        TransactionRules.AssertValidFlow(null);
        TransactionRules.AssertValidFlow(TransactionFlow.Transfer);
        Assert.Throws<AppValidationException>(() => TransactionRules.AssertValidFlow("credit"));
    }
}
