using WellSpent.Application.Transactions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Transactions;

public sealed class TransactionFlowTests
{
    [Theory]
    [InlineData("payment", 10, TransactionFlow.Payment)]
    [InlineData("income", -10, TransactionFlow.Income)]
    [InlineData("transfer", -10, TransactionFlow.Transfer)]
    [InlineData("loan", 10, TransactionFlow.Spend)] // loan payments count, as today
    [InlineData("groceries", -10, TransactionFlow.Refund)]
    [InlineData(null, 0, TransactionFlow.Spend)]
    public void Infer_FromSystemKeyThenSign(string? key, int amount, string expected) =>
        Assert.Equal(expected, TransactionFlow.Infer(key, amount));

    [Theory]
    [InlineData(TransactionFlow.Spend, true)]
    [InlineData(TransactionFlow.Refund, true)]
    [InlineData(TransactionFlow.Transfer, true)]
    [InlineData(TransactionFlow.Income, false)]
    [InlineData(TransactionFlow.Payment, false)]
    public void CountsAsSpending(string flow, bool counts) => Assert.Equal(counts, TransactionFlow.CountsAsSpending(flow));

    [Fact]
    public void OnCategoryChange_IntoPaymentAlwaysWins() =>
        Assert.Equal(TransactionFlow.Payment, TransactionFlow.OnCategoryChange(TransactionFlow.Transfer, null, "payment", 10m));

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
