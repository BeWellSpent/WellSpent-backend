using WellSpent.Application.Plaid;
using Xunit;

namespace WellSpent.Application.Tests.Plaid;

/// <summary>Mirrors internal/plaid/account.go — untested in Go itself, but trivial pure logic worth covering here.</summary>
public sealed class PlaidAccountMappingTests
{
    [Theory]
    [InlineData("credit", "", 2)]
    [InlineData("investment", "", 7)]
    [InlineData("brokerage", "", 7)]
    [InlineData("depository", "savings", 5)]
    [InlineData("depository", "checking", 3)]
    [InlineData("depository", "", 3)]
    [InlineData("loan", "", 5)]
    [InlineData("other", "", 5)]
    [InlineData("CREDIT", "", 2)]
    [InlineData("depository", "SAVINGS", 5)]
    public void PaymentTypeId_MapsAsExpected(string accountType, string accountSubtype, int expected)
    {
        Assert.Equal(expected, PlaidAccountMapping.PaymentTypeId(accountType, accountSubtype));
    }

    [Fact]
    public void AccountName_NoMask_ReturnsBareName()
    {
        Assert.Equal("Chase Checking", PlaidAccountMapping.AccountName("Chase Checking", ""));
        Assert.Equal("Chase Checking", PlaidAccountMapping.AccountName("Chase Checking", null));
    }

    [Fact]
    public void AccountName_WithMask_AppendsMiddleDotsAndDigits()
    {
        Assert.Equal("Chase Checking ···1234", PlaidAccountMapping.AccountName("Chase Checking", "1234"));
    }
}
