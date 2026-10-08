using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Notifications.DeleteAlertSubscription;
using WellSpent.Application.Notifications.GetUnreadCount;
using WellSpent.Application.Notifications.ListAlertSubscriptions;
using WellSpent.Application.Notifications.ListNotifications;
using WellSpent.Application.Notifications.MarkNotificationsRead;
using WellSpent.Application.Notifications.RegisterDeviceToken;
using WellSpent.Application.Notifications.UpsertAlertSubscription;

namespace WellSpent.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/notifications").WithTags("notifications").RequireAuthorization();

        group.MapGet("", async (Guid? budgetProfileId, int? limit, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListNotificationsQuery(CurrentUser.GetId(ctx), budgetProfileId, limit ?? 0), ct);
            return Results.Ok(new { notifications = result.Notifications, unreadCount = result.UnreadCount });
        });

        group.MapPost("/mark-read", async (MarkReadRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new MarkNotificationsReadCommand(CurrentUser.GetId(ctx), body.Ids ?? []), ct);
            return Results.Ok();
        });

        group.MapGet("/unread-count", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var count = await sender.Send(new GetUnreadCountQuery(CurrentUser.GetId(ctx)), ct);
            return Results.Ok(new { count });
        });

        group.MapGet("/alert-subscriptions", async (Guid budgetProfileId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var subscriptions = await sender.Send(new ListAlertSubscriptionsQuery(CurrentUser.GetId(ctx), budgetProfileId), ct);
            return Results.Ok(new { subscriptions });
        });

        group.MapPut("/alert-subscriptions", async (UpsertSubscriptionRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var subscription = await sender.Send(new UpsertAlertSubscriptionCommand(
                CurrentUser.GetId(ctx), body.BudgetProfileId, body.AlertType, body.Channel,
                body.ThresholdPct, body.ThresholdScope ?? "", body.CategoryId, body.NotifyAllMembers), ct);
            return Results.Ok(new { subscription });
        });

        group.MapDelete("/alert-subscriptions/{id:guid}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteAlertSubscriptionCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok();
        });

        group.MapPost("/device-tokens", async (RegisterDeviceTokenRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RegisterDeviceTokenCommand(CurrentUser.GetId(ctx), body.Token, body.Platform), ct);
            return Results.Ok();
        });
    }

    private sealed record MarkReadRequestBody(List<Guid>? Ids);

    private sealed record UpsertSubscriptionRequestBody(
        Guid BudgetProfileId, string AlertType, string Channel, decimal ThresholdPct,
        string? ThresholdScope, int? CategoryId, bool NotifyAllMembers);

    private sealed record RegisterDeviceTokenRequestBody(string Token, string Platform);
}
