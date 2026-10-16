using NSubstitute;
using WellSpent.Application.Notifications.DeleteAlertSubscription;
using WellSpent.Application.Notifications.RegisterDeviceToken;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;
using Xunit;

namespace WellSpent.Application.Tests.Notifications;

public sealed class DeleteAlertSubscriptionCommandHandlerTests
{
    [Fact]
    public async Task PassesIdAndUserIdThrough()
    {
        var notifications = Substitute.For<INotificationRepository>();
        var userId = Guid.NewGuid();
        var subId = Guid.NewGuid();

        await new DeleteAlertSubscriptionCommandHandler(notifications).Handle(
            new DeleteAlertSubscriptionCommand(userId, subId), CancellationToken.None);

        await notifications.Received(1).DeleteSubscriptionAsync(subId, userId, Arg.Any<CancellationToken>());
    }
}

public sealed class RegisterDeviceTokenCommandHandlerTests
{
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private RegisterDeviceTokenCommandHandler CreateHandler() => new(_notifications);

    [Fact]
    public async Task NonIosPlatform_Throws()
    {
        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new RegisterDeviceTokenCommand(Guid.NewGuid(), "token-value", "android"), CancellationToken.None));
    }

    [Fact]
    public async Task EmptyToken_Throws()
    {
        await Assert.ThrowsAsync<AppValidationException>(
            () => CreateHandler().Handle(new RegisterDeviceTokenCommand(Guid.NewGuid(), "", "ios"), CancellationToken.None));
    }

    [Fact]
    public async Task Success_Upserts()
    {
        var userId = Guid.NewGuid();

        await CreateHandler().Handle(new RegisterDeviceTokenCommand(userId, "device-token", "ios"), CancellationToken.None);

        await _notifications.Received(1).UpsertDeviceTokenAsync(userId, "ios", "device-token", Arg.Any<CancellationToken>());
    }
}
