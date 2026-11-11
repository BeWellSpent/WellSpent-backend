using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Entities;
using WellSpent.Infrastructure;
using WellSpent.Infrastructure.Persistence;
using Xunit;

namespace WellSpent.Infrastructure.Tests.Persistence;

public sealed class CategoryListingTests
{
    [Fact]
    public async Task ListCategories_HidesRetiredSystemCategories()
    {
        var userId = Guid.NewGuid();
        var db = new WellSpentDbContext(new DbContextOptionsBuilder<WellSpentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Categories.AddRange(
            new Category { Id = 1, Name = "Groceries", IsSystem = true, SystemKey = "groceries" },
            new Category { Id = 2, Name = "Payment", IsSystem = true, SystemKey = "payment", IsActive = false },
            new Category { Id = 3, Name = "Coffee", UserId = userId },
            new Category { Id = 4, Name = "Old", UserId = userId, IsActive = false });
        await db.SaveChangesAsync();

        var listed = await new TransactionRepository(db).ListCategoriesAsync(userId, CancellationToken.None);

        Assert.Equal(["Coffee", "Groceries"], listed.Select(c => c.Name));
    }
}
