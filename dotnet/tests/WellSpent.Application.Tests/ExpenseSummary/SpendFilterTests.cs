using WellSpent.Application.ExpenseSummary;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.ExpenseSummary;

public sealed class SpendFilterTests
{
    [Fact]
    public void NonSpendCategoryIds_ResolvesIncomeAndPayment_NotTransfer()
    {
        var systemCategories = new Dictionary<string, int> { ["income"] = 1, ["payment"] = 2, ["transfer"] = 3 };

        var ids = SpendFilter.NonSpendCategoryIds(systemCategories);

        Assert.Contains(1, ids);
        Assert.Contains(2, ids);
        Assert.DoesNotContain(3, ids);
    }

    [Fact]
    public void NonSpendCategoryIds_MissingKey_IsSkippedNotError()
    {
        var ids = SpendFilter.NonSpendCategoryIds(new Dictionary<string, int>());
        Assert.Empty(ids);
    }

    [Fact]
    public void IsNonSpendTransaction_ExcludedFlag()
    {
        var tx = new Transaction { IsExcluded = true, CategoryId = 99 };
        Assert.True(SpendFilter.IsNonSpendTransaction(tx, new HashSet<int>()));
    }

    [Fact]
    public void IsNonSpendTransaction_NonSpendCategory()
    {
        var tx = new Transaction { CategoryId = 5 };
        Assert.True(SpendFilter.IsNonSpendTransaction(tx, new HashSet<int> { 5 }));
    }

    [Fact]
    public void IsNonSpendTransaction_OrdinaryTransaction_IsSpend()
    {
        var tx = new Transaction { CategoryId = 5 };
        Assert.False(SpendFilter.IsNonSpendTransaction(tx, new HashSet<int> { 6 }));
    }

    [Fact]
    public void IsUnpaidFixed_TrueOnlyForUnpaidFixedType()
    {
        Assert.True(SpendFilter.IsUnpaidFixed(new Transaction { TransactionTypeId = 1, IsPaid = false }));
        Assert.False(SpendFilter.IsUnpaidFixed(new Transaction { TransactionTypeId = 1, IsPaid = true }));
        Assert.False(SpendFilter.IsUnpaidFixed(new Transaction { TransactionTypeId = 2, IsPaid = false }));
    }

    [Fact]
    public void IsFixed_MatchesTransactionTypeOne()
    {
        Assert.True(SpendFilter.IsFixed(new Transaction { TransactionTypeId = 1 }));
        Assert.False(SpendFilter.IsFixed(new Transaction { TransactionTypeId = 2 }));
        Assert.False(SpendFilter.IsFixed(new Transaction { TransactionTypeId = null }));
    }
}
