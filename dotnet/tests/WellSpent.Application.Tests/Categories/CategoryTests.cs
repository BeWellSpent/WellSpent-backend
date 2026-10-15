using NSubstitute;
using WellSpent.Application.Categories.CreateCategory;
using WellSpent.Application.Categories.DeleteCategory;
using WellSpent.Application.Categories.ListCategories;
using WellSpent.Application.Categories.UpdateCategory;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Categories;

public sealed class CategoryTests
{
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();

    [Fact]
    public async Task List_NoBudgetFilter_UsesPlainList()
    {
        var userId = Guid.NewGuid();
        _transactions.ListCategoriesAsync(userId, Arg.Any<CancellationToken>())
            .Returns([new Category { Id = 1, Name = "Groceries", IsSystem = true }]);

        var result = await new ListCategoriesQueryHandler(_transactions).Handle(new ListCategoriesQuery(userId, null), CancellationToken.None);

        Assert.Single(result);
        await _transactions.DidNotReceive().ListCategoriesForBudgetAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_WithBudgetFilter_UsesBudgetVariant()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _transactions.ListCategoriesForBudgetAsync(userId, profileId, Arg.Any<CancellationToken>()).Returns([]);

        await new ListCategoriesQueryHandler(_transactions).Handle(new ListCategoriesQuery(userId, profileId), CancellationToken.None);

        await _transactions.Received(1).ListCategoriesForBudgetAsync(userId, profileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ScopedToCaller()
    {
        var userId = Guid.NewGuid();
        _transactions.CreateCategoryAsync("Hobbies", userId, "#FF0000", Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 5, Name = "Hobbies", UserId = userId, Color = "#FF0000" });

        var result = await new CreateCategoryCommandHandler(_transactions).Handle(new CreateCategoryCommand(userId, "Hobbies", "#FF0000"), CancellationToken.None);

        Assert.Equal("Hobbies", result.Name);
        Assert.False(result.IsSystem);
    }

    [Fact]
    public async Task Update_SystemCategory_GoesThroughColorOnlyPath()
    {
        var userId = Guid.NewGuid();
        _transactions.GetCategoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 1, Name = "Groceries", IsSystem = true, SystemKey = "groceries" });
        _transactions.UpdateSystemCategoryColorAsync(1, "#00FF00", Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 1, Name = "Groceries", IsSystem = true, Color = "#00FF00", SystemKey = "groceries" });

        var result = await new UpdateCategoryCommandHandler(_transactions).Handle(new UpdateCategoryCommand(userId, 1, "Groceries", "#00FF00"), CancellationToken.None);

        Assert.Equal("#00FF00", result.Color);
        await _transactions.DidNotReceive().UpdateCategoryAsync(Arg.Any<int>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AnotherUsersCategory_ThrowsNotFound_NotForbidden()
    {
        // Mirrors Go: the repository's WHERE clause scopes by (id, userId,
        // not-system), so another user's category simply never matches.
        var userId = Guid.NewGuid();
        _transactions.GetCategoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 1, Name = "Mine", IsSystem = false, UserId = Guid.NewGuid() });
        _transactions.UpdateCategoryAsync(1, userId, "Mine", "#000", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Category>(new NotFoundException("category", "1")));

        await Assert.ThrowsAsync<NotFoundException>(() => new UpdateCategoryCommandHandler(_transactions)
            .Handle(new UpdateCategoryCommand(userId, 1, "Mine", "#000"), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_SystemCategory_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        _transactions.GetCategoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 1, Name = "Groceries", IsSystem = true });

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteCategoryCommandHandler(_transactions)
            .Handle(new DeleteCategoryCommand(userId, 1, 2), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_NotOwner_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        _transactions.GetCategoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 1, Name = "Mine", IsSystem = false, UserId = Guid.NewGuid() });

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteCategoryCommandHandler(_transactions)
            .Handle(new DeleteCategoryCommand(userId, 1, 2), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ReplacementNotAccessible_ThrowsForbidden()
    {
        var userId = Guid.NewGuid();
        _transactions.GetCategoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 1, Name = "Mine", IsSystem = false, UserId = userId });
        _transactions.GetCategoryAsync(2, Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 2, Name = "SomeoneElse's", IsSystem = false, UserId = Guid.NewGuid() });

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteCategoryCommandHandler(_transactions)
            .Handle(new DeleteCategoryCommand(userId, 1, 2), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ReplacementIsGlobalSystemCategory_Allowed()
    {
        var userId = Guid.NewGuid();
        _transactions.GetCategoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 1, Name = "Mine", IsSystem = false, UserId = userId });
        _transactions.GetCategoryAsync(2, Arg.Any<CancellationToken>())
            .Returns(new Category { Id = 2, Name = "Groceries", IsSystem = true, UserId = null });

        await new DeleteCategoryCommandHandler(_transactions).Handle(new DeleteCategoryCommand(userId, 1, 2), CancellationToken.None);

        await _transactions.Received(1).DeleteCategoryAndReassignAsync(1, userId, 2, Arg.Any<CancellationToken>());
    }
}
