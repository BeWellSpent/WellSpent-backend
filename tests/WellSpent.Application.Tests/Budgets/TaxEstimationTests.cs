using WellSpent.Application.Budgets;
using Xunit;

namespace WellSpent.Application.Tests.Budgets;

public sealed class TaxEstimationTests
{
    [Fact]
    public void Estimate_SingleNoState()
    {
        var r = TaxEstimation.Estimate(100_000m, "", 1);
        Assert.InRange(r.FederalTax, 13614m - 5m, 13614m + 5m);
        Assert.Equal(0m, r.StateTax);
        Assert.Equal(r.TotalAnnual / 12, r.MonthlySaving);
    }

    [Fact]
    public void Estimate_MfjWithCaState()
    {
        var r = TaxEstimation.Estimate(200_000m, "CA", 2);
        Assert.True(r.FederalTax > 0);
        Assert.True(r.StateTax > 0);
        Assert.Equal(r.FederalTax + r.StateTax, r.TotalAnnual);
    }

    [Fact]
    public void Estimate_NoIncome()
    {
        var r = TaxEstimation.Estimate(0m, "NY", 1);
        Assert.Equal(0m, r.FederalTax);
        Assert.Equal(0m, r.StateTax);
        Assert.Equal(0m, r.TotalAnnual);
    }

    [Fact]
    public void Estimate_NoTaxState()
    {
        var r = TaxEstimation.Estimate(80_000m, "TX", 1);
        Assert.True(r.FederalTax > 0);
        Assert.Equal(0m, r.StateTax);
    }

    [Fact]
    public void Estimate_UnknownStateCode()
    {
        var r = TaxEstimation.Estimate(80_000m, "XX", 1);
        Assert.Equal(0m, r.StateTax);
        Assert.True(r.FederalTax > 0);
    }

    [Fact]
    public void Estimate_BelowStandardDeduction()
    {
        var r = TaxEstimation.Estimate(10_000m, "", 1);
        Assert.Equal(0m, r.FederalTax);
    }

    [Fact]
    public void ComputeStateTax_FlatRate()
    {
        var tax = TaxEstimation.ComputeStateTax("CO", 50_000m);
        Assert.InRange(tax, 2200m - 0.01m, 2200m + 0.01m);
    }

    [Fact]
    public void ComputeStateTax_Progressive()
    {
        var tax = TaxEstimation.ComputeStateTax("CA", 80_000m);
        Assert.True(tax > 0);
    }

    [Fact]
    public void ComputeStateTax_UnspecifiedFilingStatus_FallsBackToSingle()
    {
        var withUnspecified = TaxEstimation.Estimate(100_000m, "", 0);
        var withSingle = TaxEstimation.Estimate(100_000m, "", 1);
        Assert.Equal(withSingle.FederalTax, withUnspecified.FederalTax);
    }
}
