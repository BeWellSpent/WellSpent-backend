using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.FixedExpenses;

public sealed class FixedExpenseOccurrenceMatchingTests
{
    private static Transaction Tx(Guid id, DateOnly? date) => new() { Id = id, Date = date };

    [Fact]
    public void NoCandidates_ReturnsNull()
    {
        Assert.Null(FixedExpenseOccurrenceMatching.ClosestTo([], new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void NoTargetDate_FallsBackToLatest()
    {
        var early = Tx(Guid.NewGuid(), new DateOnly(2026, 10, 1));
        var late = Tx(Guid.NewGuid(), new DateOnly(2026, 10, 29));

        Assert.Equal(late.Id, FixedExpenseOccurrenceMatching.ClosestTo([early, late], null)!.Id);
    }

    [Fact]
    public void ThreeBiweeklyOccurrencesInOnePeriod_EachRealPaymentMatchesItsOwnDate()
    {
        // The reported bug: a biweekly bill landing on the 1st, 15th, and 29th
        // in one month used to always resolve to the latest unpaid occurrence,
        // cross-matching the 1st's real payment to the 29th's occurrence and
        // the 29th's real payment to the already-unpaid 1st.
        var oct1 = Tx(Guid.NewGuid(), new DateOnly(2026, 10, 1));
        var oct15 = Tx(Guid.NewGuid(), new DateOnly(2026, 10, 15));
        var oct29 = Tx(Guid.NewGuid(), new DateOnly(2026, 10, 29));
        var all = new List<Transaction> { oct1, oct15, oct29 };

        Assert.Equal(oct1.Id, FixedExpenseOccurrenceMatching.ClosestTo(all, new DateOnly(2026, 10, 1))!.Id);
        Assert.Equal(oct15.Id, FixedExpenseOccurrenceMatching.ClosestTo(all, new DateOnly(2026, 10, 15))!.Id);
        Assert.Equal(oct29.Id, FixedExpenseOccurrenceMatching.ClosestTo(all, new DateOnly(2026, 10, 28))!.Id);
    }

    [Fact]
    public void TargetDateBetweenTwoCandidates_PicksTheNearerOne()
    {
        var earlier = Tx(Guid.NewGuid(), new DateOnly(2026, 10, 1));
        var later = Tx(Guid.NewGuid(), new DateOnly(2026, 10, 15));

        Assert.Equal(earlier.Id, FixedExpenseOccurrenceMatching.ClosestTo([earlier, later], new DateOnly(2026, 10, 5))!.Id);
        Assert.Equal(later.Id, FixedExpenseOccurrenceMatching.ClosestTo([earlier, later], new DateOnly(2026, 10, 10))!.Id);
    }
}
