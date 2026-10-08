using MediatR;
using WellSpent.Application.Auth.AppleSignIn;
using WellSpent.Application.Auth.GoogleAuthUrl;
using WellSpent.Application.Auth.GoogleExchange;
using WellSpent.Application.Auth.Login;
using WellSpent.Application.Auth.Logout;
using WellSpent.Application.Auth.RefreshToken;
using WellSpent.Application.Auth.Register;
using WellSpent.Application.Auth.ResendVerificationEmail;
using WellSpent.Application.Auth.VerifyEmail;

namespace WellSpent.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/auth").WithTags("auth");

        group.MapPost("/register", async (RegisterRequestBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RegisterCommand(
                body.Email, body.Password, body.FirstName, body.LastName,
                body.CountryCode, body.StateCode, body.Language, body.Currency, body.CaptchaToken), ct);
            return Results.Ok(new TokenResponse(result.AccessToken, "bearer", result.ExpiresIn));
        });

        group.MapPost("/login", async (LoginRequestBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new LoginCommand(body.Email, body.Password, body.RememberMe), ct);
            return Results.Ok(new LoginResponse(result.AccessToken, "bearer", result.ExpiresIn, result.Language, result.Currency));
        });

        // JWT is stateless; invalidation is handled client-side by discarding the token.
        group.MapPost("/logout", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new LogoutCommand(), ct);
            return Results.Ok();
        });

        group.MapPost("/refresh-token", async (RefreshTokenRequestBody body, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var result = await sender.Send(new RefreshTokenCommand(body.RefreshToken), ct);
                return Results.Ok(result);
            }
            catch (NotImplementedException)
            {
                return Results.StatusCode(StatusCodes.Status501NotImplemented);
            }
        });

        group.MapGet("/google/url", async (string state, ISender sender, CancellationToken ct) =>
        {
            var url = await sender.Send(new GetGoogleAuthUrlQuery(state), ct);
            return Results.Ok(new { url });
        });

        group.MapPost("/google/exchange", async (GoogleExchangeRequestBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ExchangeGoogleCodeCommand(body.Code, body.RedirectUri, body.Language, body.Currency), ct);
            return Results.Ok(new OAuthSignInResponse(result.AccessToken, result.ExpiresIn, result.IsNewUser, result.Language, result.Currency));
        });

        group.MapPost("/apple/sign-in", async (AppleSignInRequestBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new SignInWithAppleCommand(
                body.IdentityToken, body.AuthorizationCode, body.FirstName, body.LastName, body.Language, body.Currency), ct);
            return Results.Ok(new OAuthSignInResponse(result.AccessToken, result.ExpiresIn, result.IsNewUser, result.Language, result.Currency));
        });

        group.MapPost("/verify-email", async (VerifyEmailRequestBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new VerifyEmailCommand(body.Token), ct);
            return Results.Ok(new { success = true });
        });

        group.MapPost("/resend-verification-email", async (ResendVerificationEmailRequestBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ResendVerificationEmailCommand(body.Email), ct);
            return Results.Ok();
        });
    }

    private sealed record RegisterRequestBody(
        string Email, string Password, string FirstName, string LastName,
        string CountryCode, string StateCode, string Language, string Currency, string CaptchaToken);

    private sealed record LoginRequestBody(string Email, string Password, bool RememberMe);

    private sealed record RefreshTokenRequestBody(string RefreshToken);

    private sealed record GoogleExchangeRequestBody(string Code, string RedirectUri, string Language, string Currency);

    private sealed record AppleSignInRequestBody(
        string IdentityToken, string AuthorizationCode, string FirstName, string LastName, string Language, string Currency);

    private sealed record VerifyEmailRequestBody(string Token);

    private sealed record ResendVerificationEmailRequestBody(string Email);

    private sealed record TokenResponse(string AccessToken, string TokenType, long ExpiresIn);

    private sealed record LoginResponse(string AccessToken, string TokenType, long ExpiresIn, string Language, string Currency);

    private sealed record OAuthSignInResponse(string AccessToken, long ExpiresIn, bool IsNewUser, string Language, string Currency);
}
