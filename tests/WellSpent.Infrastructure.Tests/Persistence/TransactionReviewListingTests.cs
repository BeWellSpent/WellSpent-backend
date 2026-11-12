using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Entities;
using WellSpent.Infrastructure;
using WellSpent.Infrastructure.Persistence;
using Xunit;

namespace WellSpent.Infrastructure.Tests.Persistence;

public sealed class TransactionReviewListingTests
{
    [Fact]
    public async Task List_IncludesPendingReviewsInClosedPeriods_ButNotDismissed() // #120
    {
        var db = new WellSpentDbContext(new DbContextOptionsBuilder<WellSpentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var profileId = Guid.NewGuid();
        var closed = new BudgetPeriod { Id = Guid.NewGuid(), BudgetProfileId = profileId, StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2026, 8, 31), IsArchived = true };
        db.BudgetPeriods.Add(closed);
        TransactionReview Review(string status)
        {
            var spend = new Transaction { Id = Guid.NewGuid(), Name = "Zelle", Amount = 1850m, BudgetPeriodId = closed.Id, Flow = TransactionFlow.Transfer };
            var bill = new Transaction { Id = Guid.NewGuid(), Name = "Rent", Amount = 1850m, BudgetPeriodId = closed.Id, TransactionTypeId = 1, Flow = TransactionFlow.Spend };
            db.Transactions.AddRange(spend, bill);
            var review = new TransactionReview { Id = Guid.NewGuid(), BudgetPeriodId = closed.Id, TransactionId = spend.Id, MatchedTransactionId = bill.Id, MatchScore = 90m, Status = status };
            db.TransactionReviews.Add(review);
            return review;
        }
        var pending = Review("pending");
        Review("dismissed");
        await db.SaveChangesAsync();

        var listed = await new TransactionReviewRepository(db).ListAsync(profileId, CancellationToken.None);

        Assert.Equal([pending.Id], listed.Select(r => r.Id));
    }
}
