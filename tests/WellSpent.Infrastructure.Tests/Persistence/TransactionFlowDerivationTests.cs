using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Entities;
using WellSpent.Infrastructure;
using Xunit;

namespace WellSpent.Infrastructure.Tests.Persistence;

/// <summary>A row saved without a flow gets one from its sign; categories never decide it (#119).</summary>
public sealed class TransactionFlowDerivationTests
{
    private const int Groceries = 1, Dining = 5;
    private readonly WellSpentDbContext _db = new(new DbContextOptionsBuilder<WellSpentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private async Task<Transaction> Add(int? category, decimal amount, string? flow = null)
    {
        var tx = new Transaction { Id = Guid.NewGuid(), CategoryId = category, Amount = amount, Flow = flow };
        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }

    [Theory]
    [InlineData(80, TransactionFlow.Spend)]
    [InlineData(-20, TransactionFlow.Refund)]
    public async Task Added_WithoutAFlow_FollowsTheSign(int amount, string expected) =>
        Assert.Equal(expected, (await Add(Groceries, amount)).Flow);

    [Fact]
    public async Task Added_ExplicitFlowIsKept() =>
        Assert.Equal(TransactionFlow.Payment, (await Add(null, 500m, TransactionFlow.Payment)).Flow);

    [Fact]
    public async Task CategoryChange_NeverChangesTheFlow()
    {
        var venmo = await Add(null, -12m, TransactionFlow.Transfer);
        venmo.CategoryId = Dining; // a friend paying back drinks: still a transfer, now in Dining
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Transfer, venmo.Flow);
    }

    [Fact]
    public async Task AmountSignFlip_MovesSpendToRefund_ButNotIncome()
    {
        var spend = await Add(Groceries, 30m);
        var income = await Add(null, -3000m, TransactionFlow.Income);
        spend.Amount = -30m;
        income.Amount = 3000m;
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Refund, spend.Flow);
        Assert.Equal(TransactionFlow.Income, income.Flow);
    }

    [Fact]
    public async Task ExplicitFlowOnUpdate_IsKept()
    {
        var tx = await Add(Groceries, 30m);
        tx.Flow = TransactionFlow.Transfer;
        tx.Amount = -30m;
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Transfer, tx.Flow);
    }
}
