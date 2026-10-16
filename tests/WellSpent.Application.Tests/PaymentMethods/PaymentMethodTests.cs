using NSubstitute;
using WellSpent.Application.Common;
using WellSpent.Application.PaymentMethods;
using WellSpent.Application.PaymentMethods.CreatePaymentMethod;
using WellSpent.Application.PaymentMethods.DeletePaymentMethod;
using WellSpent.Application.PaymentMethods.ListPaymentMethods;
using WellSpent.Application.PaymentMethods.UpdatePaymentMethod;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.PaymentMethods;

public sealed class PaymentTypeMappingTests
{
    [Theory]
    [InlineData(1, "cash")]
    [InlineData(2, "credit")]
    [InlineData(3, "debit")]
    [InlineData(4, "digital_wallet")]
    [InlineData(5, "bank_transfer")]
    [InlineData(6, "crypto")]
    [InlineData(7, "investment")]
    [InlineData(8, "other")]
    [InlineData(0, "unspecified")]
    [InlineData(null, "unspecified")]
    public void ToName_MatchesSeededOrder(int? id, string expected) =>
        Assert.Equal(expected, PaymentTypeMapping.ToName(id));

    [Theory]
    [InlineData("credit", 2)]
    [InlineData("other", 8)]
    [InlineData("bogus", 0)]
    public void ToId_RoundTrips(string name, int expected) =>
        Assert.Equal(expected, PaymentTypeMapping.ToId(name));
}

public sealed class PaymentMethodTests
{
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly IBudgetProfileRepository _profiles = Substitute.For<IBudgetProfileRepository>();
    private BudgetAccessGuard Access => new(_profiles);

    [Fact]
    public async Task List_ReturnsMappedMethods()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        _transactions.ListPaymentMethodsAsync(profileId, Arg.Any<CancellationToken>()).Returns([
            new PaymentMethod { Id = Guid.NewGuid(), Name = "Chase Visa", PaymentTypeId = 2, BudgetPersonId = 3 },
        ]);

        var result = await new ListPaymentMethodsQueryHandler(_transactions).Handle(new ListPaymentMethodsQuery(userId, profileId), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("credit", result[0].Type);
        Assert.Equal(3, result[0].BudgetPersonId);
    }

    [Fact]
    public async Task Create_ZeroBudgetPersonId_Throws()
    {
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<AppValidationException>(() => new CreatePaymentMethodCommandHandler(_transactions)
            .Handle(new CreatePaymentMethodCommand(userId, "Cash", "cash", 0, ""), CancellationToken.None));
    }

    [Fact]
    public async Task Create_Success_SetsUserIdToCaller()
    {
        var userId = Guid.NewGuid();
        PaymentMethod? captured = null;
        _transactions.CreatePaymentMethodAsync(Arg.Any<PaymentMethod>(), Arg.Any<CancellationToken>())
            .Returns(ci => { captured = ci.Arg<PaymentMethod>(); return captured; });

        var result = await new CreatePaymentMethodCommandHandler(_transactions)
            .Handle(new CreatePaymentMethodCommand(userId, "Chase Visa", "credit", 3, "#123"), CancellationToken.None);

        Assert.Equal("credit", result.Type);
        Assert.Equal(userId, captured!.UserId);
        Assert.Equal(2, captured.PaymentTypeId);
    }

    [Fact]
    public async Task Update_LinkedToPerson_RequiresCollaboratorOnThatPersonsBudget()
    {
        var methodId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        _transactions.GetPaymentMethodAsync(methodId, Arg.Any<CancellationToken>())
            .Returns(new PaymentMethod { Id = methodId, Name = "x", BudgetPersonId = 5 });
        _profiles.GetPersonByIdAsync(5, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 5, BudgetProfileId = profileId, Role = "collaborator" });
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = Guid.NewGuid(), Name = "x" });
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 9, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new UpdatePaymentMethodCommandHandler(_transactions, _profiles, Access)
            .Handle(new UpdatePaymentMethodCommand(viewerId, methodId, "x", "#000", ""), CancellationToken.None));
    }

    [Fact]
    public async Task Update_UnlinkedMethod_OnlyOwnerAllowed()
    {
        var methodId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        _transactions.GetPaymentMethodAsync(methodId, Arg.Any<CancellationToken>())
            .Returns(new PaymentMethod { Id = methodId, Name = "Cash", UserId = ownerId, BudgetPersonId = null });

        await Assert.ThrowsAsync<ForbiddenException>(() => new UpdatePaymentMethodCommandHandler(_transactions, _profiles, Access)
            .Handle(new UpdatePaymentMethodCommand(strangerId, methodId, "Cash", "#000", ""), CancellationToken.None));
    }

    [Fact]
    public async Task Update_EmptyAlias_ClearsIt()
    {
        var methodId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        _transactions.GetPaymentMethodAsync(methodId, Arg.Any<CancellationToken>())
            .Returns(new PaymentMethod { Id = methodId, Name = "Cash", UserId = ownerId, Alias = "Old alias" });
        string? capturedAlias = "unset";
        _transactions.UpdatePaymentMethodAsync(methodId, "Cash", "#000", Arg.Do<string?>(a => capturedAlias = a), Arg.Any<CancellationToken>())
            .Returns(new PaymentMethod { Id = methodId, Name = "Cash", UserId = ownerId });

        await new UpdatePaymentMethodCommandHandler(_transactions, _profiles, Access)
            .Handle(new UpdatePaymentMethodCommand(ownerId, methodId, "Cash", "#000", ""), CancellationToken.None);

        Assert.Null(capturedAlias);
    }

    [Fact]
    public async Task Delete_RequiresCollaboratorOnGivenProfile_ThenVerifiesBothIdsExist()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var replacementId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x" });
        _transactions.GetPaymentMethodAsync(id, Arg.Any<CancellationToken>()).Returns(new PaymentMethod { Id = id, Name = "x" });
        _transactions.GetPaymentMethodAsync(replacementId, Arg.Any<CancellationToken>()).Returns(new PaymentMethod { Id = replacementId, Name = "y" });

        await new DeletePaymentMethodCommandHandler(_transactions, Access)
            .Handle(new DeletePaymentMethodCommand(ownerId, id, replacementId, profileId), CancellationToken.None);

        await _transactions.Received(1).DeletePaymentMethodAndReassignAsync(id, replacementId, profileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_NonCollaborator_ThrowsForbidden_NeverTouchesPaymentMethods()
    {
        var profileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        _profiles.GetByIdAsync(profileId, Arg.Any<CancellationToken>())
            .Returns(new BudgetProfile { Id = profileId, UserId = ownerId, Name = "x" });
        _profiles.GetPersonByUserIdAsync(profileId, viewerId, Arg.Any<CancellationToken>())
            .Returns(new BudgetPerson { Id = 2, BudgetProfileId = profileId, UserId = viewerId, Role = "viewer" });

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeletePaymentMethodCommandHandler(_transactions, Access)
            .Handle(new DeletePaymentMethodCommand(viewerId, Guid.NewGuid(), Guid.NewGuid(), profileId), CancellationToken.None));

        await _transactions.DidNotReceive().GetPaymentMethodAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
