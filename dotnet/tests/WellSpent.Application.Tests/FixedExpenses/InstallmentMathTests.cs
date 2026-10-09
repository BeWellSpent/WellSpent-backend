using WellSpent.Application.FixedExpenses;
using Xunit;

namespace WellSpent.Application.Tests.FixedExpenses;

public sealed class InstallmentMathTests
{
    [Fact]
    public void InstallmentAmount_EvenSplit()
    {
        Assert.Equal(300.00m, InstallmentMath.InstallmentAmount(900.00m, 3));
    }

    [Fact]
    public void InstallmentAmount_RoundsToNearestCentDown()
    {
        // 1000/3 = 333.333... -> 333.33. The one-cent residue is structural:
        // a fixed_expense carries a single planned_amount every payment
        // inherits, so no single payment can absorb the difference.
        Assert.Equal(333.33m, InstallmentMath.InstallmentAmount(1000.00m, 3));
    }

    [Fact]
    public void InstallmentAmount_RoundsToNearestCentUp()
    {
        // 1000/7 = 142.857... -> 142.86
        Assert.Equal(142.86m, InstallmentMath.InstallmentAmount(1000.00m, 7));
    }

    [Fact]
    public void InstallmentAmount_ExactHalfCentRoundsAwayFromZero()
    {
        // 0.05/2 = 0.025 exactly -> 0.03, not 0.02.
        Assert.Equal(0.03m, InstallmentMath.InstallmentAmount(0.05m, 2));
    }

    [Fact]
    public void InstallmentAmount_RoundsOnceNotTwice()
    {
        // 0.019 / 1 = 0.019 -> 0.02. Truncating to cents first would give 0.01.
        Assert.Equal(0.02m, InstallmentMath.InstallmentAmount(0.019m, 1));
    }

    [Fact]
    public void InstallmentAmount_HandlesLargeAmountsExactly()
    {
        // decimal is exact base-10 arithmetic (unlike Go's float64), so no
        // big.Int workaround is needed to stay exact at this scale.
        Assert.Equal(25000000000000000000.00m, InstallmentMath.InstallmentAmount(100000000000000000000.00m, 4));
    }

    [Fact]
    public void InstallmentAmount_ZeroPaymentsReturnsZero()
    {
        Assert.Equal(0m, InstallmentMath.InstallmentAmount(100.00m, 0));
    }

    [Fact]
    public void InstallmentEndDate_LastPaymentNotOnePast()
    {
        var first = new DateOnly(2026, 9, 18);
        // 3 payments starting September: Sep, Oct, Nov — ends in November, not December.
        Assert.Equal(new DateOnly(2026, 11, 18), InstallmentMath.InstallmentEndDate(first, 3));
    }

    [Fact]
    public void InstallmentEndDate_SinglePaymentEndsOnItself()
    {
        var first = new DateOnly(2026, 9, 18);
        Assert.Equal(first, InstallmentMath.InstallmentEndDate(first, 1));
    }
}
