using WellSpent.Application.Budgets;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

/// <summary>Mirrors Go's carryover_test.go — same scenarios, same dollar figures.</summary>
public sealed class CarryoverTests
{
    private static decimal SumRows(List<CarryoverRow> rows) => rows.Sum(r => r.Amount);

    [Fact]
    public void EvenPeriod_CreatesNothing()
    {
        var rows = Carryover.Compute(0m, new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 2000m }, 0m);
        Assert.Empty(rows);
    }

    [Fact]
    public void Leftover_SingleSavingsRowWithNoMethod()
    {
        var method = Guid.NewGuid();

        var rows = Carryover.Compute(500m, new Dictionary<Guid, decimal> { [method] = 1500m }, 0m);

        var row = Assert.Single(rows);
        Assert.Equal(500m, row.Amount);
        Assert.Equal(Carryover.SavingsSystemKey, row.CategoryKey);
        // A surplus is owed to nobody, so it must not be pinned on the method the money happened to be spent through.
        Assert.Null(row.PaymentMethodId);
    }

    [Fact]
    public void Shortfall_SingleMethodTakesAll()
    {
        var method = Guid.NewGuid();

        var rows = Carryover.Compute(-300m, new Dictionary<Guid, decimal> { [method] = 2300m }, 0m);

        var row = Assert.Single(rows);
        Assert.Equal(300m, row.Amount);
        Assert.Equal(Carryover.DebtSystemKey, row.CategoryKey);
        Assert.Equal(method, row.PaymentMethodId);
    }

    // Income 2000: 1800 debit + 500 Chase Visa -> Debt $234.78 debit, $65.22 Visa.
    [Fact]
    public void Shortfall_SplitProportionallyAcrossMethods()
    {
        var debit = Guid.NewGuid();
        var visa = Guid.NewGuid();

        var rows = Carryover.Compute(-300m, new Dictionary<Guid, decimal> { [debit] = 1800m, [visa] = 500m }, 0m);

        Assert.Equal(2, rows.Count);
        var byMethod = rows.ToDictionary(r => r.PaymentMethodId!.Value, r => r.Amount);
        Assert.All(rows, r => Assert.Equal(Carryover.DebtSystemKey, r.CategoryKey));
        Assert.Equal(234.78m, byMethod[debit]);
        Assert.Equal(65.22m, byMethod[visa]);
        Assert.Equal(300m, SumRows(rows));
    }

    [Fact]
    public void Shortfall_UnattributedSpendTakesItsShare()
    {
        var debit = Guid.NewGuid();

        var rows = Carryover.Compute(-300m, new Dictionary<Guid, decimal> { [debit] = 2100m }, 200m);

        Assert.Equal(2, rows.Count);
        var debitAmount = rows.Single(r => r.PaymentMethodId == debit).Amount;
        var unattributedAmount = rows.Single(r => r.PaymentMethodId == null).Amount;
        Assert.Equal(273.91m, debitAmount);
        Assert.Equal(26.09m, unattributedAmount);
        Assert.Equal(300m, SumRows(rows));
    }

    [Fact]
    public void Shortfall_ThreeMethodsStillSumExactly()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var rows = Carryover.Compute(-300m, new Dictionary<Guid, decimal> { [a] = 1000m, [b] = 800m, [c] = 500m }, 0m);

        Assert.Equal(3, rows.Count);
        var byMethod = rows.ToDictionary(r => r.PaymentMethodId!.Value, r => r.Amount);
        Assert.Equal(130.43m, byMethod[a]);
        Assert.Equal(104.35m, byMethod[b]);
        Assert.Equal(65.22m, byMethod[c]);
        Assert.Equal(300m, SumRows(rows));
    }

    [Fact]
    public void Shortfall_IgnoresMethodsWithNoNetSpend()
    {
        var spender = Guid.NewGuid();
        var refunded = Guid.NewGuid();

        var rows = Carryover.Compute(-300m, new Dictionary<Guid, decimal> { [spender] = 2300m, [refunded] = -50m }, 0m);

        var row = Assert.Single(rows);
        Assert.Equal(spender, row.PaymentMethodId);
        Assert.Equal(300m, row.Amount);
    }

    [Fact]
    public void Shortfall_NoSpendFallsBackToUnattributedRow()
    {
        var rows = Carryover.Compute(-300m, new Dictionary<Guid, decimal>(), 0m);

        var row = Assert.Single(rows);
        Assert.Null(row.PaymentMethodId);
        Assert.Equal(Carryover.DebtSystemKey, row.CategoryKey);
        Assert.Equal(300m, row.Amount);
    }

    [Fact]
    public void Shortfall_SubCentRemainderIsNotLost()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var shortfall = 300.0005m;

        var rows = Carryover.Compute(-shortfall, new Dictionary<Guid, decimal> { [a] = 1800m, [b] = 500m }, 0m);

        Assert.Equal(2, rows.Count);
        Assert.Equal(shortfall, SumRows(rows));
    }

    [Fact]
    public void Shortfall_IsDeterministic()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var spend = new Dictionary<Guid, decimal> { [a] = 1000m, [b] = 1000m, [c] = 300m };

        var first = Carryover.Compute(-300m, new Dictionary<Guid, decimal>(spend), 0m);
        for (var i = 0; i < 25; i++)
        {
            var got = Carryover.Compute(-300m, new Dictionary<Guid, decimal>(spend), 0m);
            Assert.Equal(first, got);
        }
    }

    [Fact]
    public void Inputs_ExcludesExcludedAndUnpaidFixed_SplitsByMethod()
    {
        var method = Guid.NewGuid();
        var txs = new List<Transaction>
        {
            new() { Amount = 100m, PaymentMethodId = method, TransactionTypeId = 2 },
            new() { Amount = 999m, IsExcluded = true, PaymentMethodId = method, TransactionTypeId = 2 },
            new() { Amount = 999m, TransactionTypeId = 1, IsPaid = false },
            new() { Amount = 50m, TransactionTypeId = 2 },
        };
        var income = new List<IncomeEntry> { new() { Amount = 2000m } };

        var (remainder, spendByMethod, unattributed) = Carryover.Inputs(txs, income, []);

        Assert.Equal(2000m - 150m, remainder);
        Assert.Equal(100m, spendByMethod[method]);
        Assert.Equal(50m, unattributed);
    }
}
