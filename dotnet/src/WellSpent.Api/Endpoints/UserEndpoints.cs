using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Users;
using WellSpent.Application.Users.ChangeEmail;
using WellSpent.Application.Users.ChangePassword;
using WellSpent.Application.Users.DeleteMe;
using WellSpent.Application.Users.GetMe;
using WellSpent.Application.Users.UpdateMe;

namespace WellSpent.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/users").WithTags("users").RequireAuthorization();

        group.MapGet("/me", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var user = await sender.Send(new GetMeQuery(CurrentUser.GetId(ctx)), ct);
            return Results.Ok(new UserResponse(user));
        });

        group.MapPut("/me", async (UpdateMeRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var user = await sender.Send(new UpdateMeCommand(
                CurrentUser.GetId(ctx), body.FirstName, body.LastName, body.CountryCode, body.StateCode,
                body.FilingStatus, body.TaxPaymentFrequency, body.Language, body.Currency), ct);
            return Results.Ok(new UserResponse(user));
        });

        group.MapPost("/me/change-password", async (ChangePasswordRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ChangePasswordCommand(CurrentUser.GetId(ctx), body.CurrentPassword, body.NewPassword), ct);
            return Results.Ok();
        });

        group.MapPost("/me/change-email", async (ChangeEmailRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var user = await sender.Send(new ChangeEmailCommand(CurrentUser.GetId(ctx), body.NewEmail), ct);
            return Results.Ok(new UserResponse(user));
        });

        group.MapDelete("/me", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteMeCommand(CurrentUser.GetId(ctx)), ct);
            return Results.Ok();
        });
    }

    private sealed record UpdateMeRequestBody(
        string FirstName, string LastName, string CountryCode, string StateCode,
        int FilingStatus, int TaxPaymentFrequency, string Language, string Currency);

    private sealed record ChangePasswordRequestBody(string CurrentPassword, string NewPassword);

    private sealed record ChangeEmailRequestBody(string NewEmail);

    private sealed record UserResponse(UserDto User);
}
