using WellSpent.Application.Reports;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Reports;

public sealed class FlowTotalsTests
{
    private static Transaction Tx(decimal amount, string flow, bool excluded = false, int type = 2, bool paid = true) =>
        new() { Id = Guid.NewGuid(), Amount = amount, Flow = flow, IsExcluded = excluded, TransactionTypeId = type, IsPaid = paid };

    [Fact]
    public void Compute_SplitsMoneyByFlow_AsPositiveMagnitudes()
    {
        var totals = FlowTotals.Compute([
            Tx(84.20m, TransactionFlow.Spend),
            Tx(-31.99m, TransactionFlow.Refund),
            Tx(-3200m, TransactionFlow.Income),
            Tx(-40m, TransactionFlow.Transfer),
            Tx(1850m, TransactionFlow.Transfer),
        ]);

        Assert.Equal(84.20m, totals.Spent.ToDecimal());
        Assert.Equal(31.99m, totals.Refunds.ToDecimal());
        Assert.Equal(3200m, totals.Income.ToDecimal());
        Assert.Equal(40m, totals.TransfersIn.ToDecimal());
        Assert.Equal(1850m, totals.TransfersOut.ToDecimal());
    }

    [Fact]
    public void Compute_CountsACardPaymentOnce_WhenBothLegsAreLinked() =>
        Assert.Equal(500m, FlowTotals.Compute([Tx(500m, TransactionFlow.Payment), Tx(-500m, TransactionFlow.Payment)]).PaidToCards.ToDecimal());

    [Fact]
    public void Compute_CountsACardPayment_WhenOnlyTheCardSideIsLinked() =>
        Assert.Equal(500m, FlowTotals.Compute([Tx(-500m, TransactionFlow.Payment)]).PaidToCards.ToDecimal());

    [Fact]
    public void Compute_SkipsExcludedRowsAndUnpaidFixedBills()
    {
        var totals = FlowTotals.Compute([
            Tx(100m, TransactionFlow.Spend, excluded: true),
            Tx(1850m, TransactionFlow.Spend, type: 1, paid: false),
            Tx(10m, TransactionFlow.Spend),
        ]);
        Assert.Equal(10m, totals.Spent.ToDecimal());
    }
}
