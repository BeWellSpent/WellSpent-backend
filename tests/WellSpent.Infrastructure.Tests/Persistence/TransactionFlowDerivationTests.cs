using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Entities;
using WellSpent.Infrastructure;
using Xunit;

namespace WellSpent.Infrastructure.Tests.Persistence;

/// <summary>Every write path gets a flow on save, matching the category rule the totals used before (#117).</summary>
public sealed class TransactionFlowDerivationTests
{
    private const int Groceries = 1, Income = 2, Payment = 3, Transfer = 4, Dining = 5;
    private readonly WellSpentDbContext _db;

    public TransactionFlowDerivationTests()
    {
        _db = new WellSpentDbContext(new DbContextOptionsBuilder<WellSpentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.Categories.AddRange(
            new Category { Id = Groceries, Name = "Groceries", IsSystem = true, SystemKey = "groceries" },
            new Category { Id = Income, Name = "Income", IsSystem = true, SystemKey = "income" },
            new Category { Id = Payment, Name = "Payment", IsSystem = true, SystemKey = "payment" },
            new Category { Id = Transfer, Name = "Transfer", IsSystem = true, SystemKey = "transfer" },
            new Category { Id = Dining, Name = "Dining out", IsSystem = false });
        _db.SaveChanges();
    }

    private async Task<Transaction> Add(int? category, decimal amount, string? flow = null)
    {
        var tx = new Transaction { Id = Guid.NewGuid(), CategoryId = category, Amount = amount, Flow = flow };
        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();
        return tx;
    }

    [Theory]
    [InlineData(Payment, 400, TransactionFlow.Payment)]
    [InlineData(Income, -3000, TransactionFlow.Income)]
    [InlineData(Transfer, 50, TransactionFlow.Transfer)]
    [InlineData(Groceries, 80, TransactionFlow.Spend)]
    [InlineData(Groceries, -20, TransactionFlow.Refund)]
    [InlineData(null, 10, TransactionFlow.Spend)]
    public async Task Added_DerivesFromCategoryAndSign(int? category, int amount, string expected) =>
        Assert.Equal(expected, (await Add(category, amount)).Flow);

    [Fact]
    public async Task Added_ExplicitFlowIsKept() =>
        Assert.Equal(TransactionFlow.Transfer, (await Add(Dining, -12m, TransactionFlow.Transfer)).Flow);

    [Fact]
    public async Task CategoryChange_IntoAndOutOfAFlowCategory_Rederives()
    {
        var tx = await Add(Groceries, 900m);
        tx.CategoryId = Payment;
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Payment, tx.Flow);

        tx.CategoryId = Groceries; // leaving Payment counts it again, as it did before flows
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Spend, tx.Flow);
    }

    [Fact]
    public async Task CategoryChange_TransferKeepsItsFlow()
    {
        var venmo = await Add(Transfer, -12m);
        venmo.CategoryId = Dining; // a friend paying back drinks: a transfer, now counted in Dining
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Transfer, venmo.Flow);
    }

    [Fact]
    public async Task CategoryChange_LeavingIncome_CountsAgain_ButAChosenIncomeStays()
    {
        var deposit = await Add(Income, -500m);
        deposit.CategoryId = Groceries; // recategorized by hand: counted again, as before flows
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Refund, deposit.Flow);

        var chosen = await Add(Dining, -500m, TransactionFlow.Income);
        chosen.CategoryId = Groceries;
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Income, chosen.Flow);
    }

    [Fact]
    public async Task AmountSignFlip_MovesSpendToRefund_ButNotIncome()
    {
        var spend = await Add(Groceries, 30m);
        var income = await Add(Income, -3000m);
        spend.Amount = -30m;
        income.Amount = 3000m;
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Refund, spend.Flow);
        Assert.Equal(TransactionFlow.Income, income.Flow);
    }

    [Fact]
    public async Task ExplicitFlowOnUpdate_SurvivesACategoryChange()
    {
        var tx = await Add(Groceries, 30m);
        tx.CategoryId = Dining;
        tx.Flow = TransactionFlow.Transfer;
        await _db.SaveChangesAsync();
        Assert.Equal(TransactionFlow.Transfer, tx.Flow);
    }
}
