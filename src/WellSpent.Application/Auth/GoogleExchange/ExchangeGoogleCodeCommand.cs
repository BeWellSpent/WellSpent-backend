using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Auth.GoogleExchange;

public sealed record ExchangeGoogleCodeCommand(
    string Code,
    string RedirectUri, // unused — mirrors the Go service method exactly, which receives but never reads it (the redirect URI is fixed at GoogleOAuth construction, not per-call)
    string Language,
    string Currency,
    string DeviceId) : IRequest<ExchangeGoogleCodeResult>;

public sealed record ExchangeGoogleCodeResult(string AccessToken, long ExpiresIn, bool IsNewUser, string Language, string Currency, string RefreshToken);

public sealed class ExchangeGoogleCodeCommandHandler(
    IUserRepository users,
    IGoogleOAuthClient google,
    IJwtService jwt,
    IRefreshTokenRepository refreshTokens,
    ILogger<ExchangeGoogleCodeCommandHandler> logger) : IRequestHandler<ExchangeGoogleCodeCommand, ExchangeGoogleCodeResult>
{
    public async Task<ExchangeGoogleCodeResult> Handle(ExchangeGoogleCodeCommand request, CancellationToken ct)
    {
        var info = await google.ExchangeCodeAsync(request.Code, ct);

        Guid userId;
        string userLang, userCurrency;
        var isNew = false;

        try
        {
            var oauthAcc = await users.GetOAuthAccountAsync(AuthConstants.OauthProviderGoogle, info.Sub, ct);
            // Returning user, resolved by Google's stable subject.
            var existing = await users.GetByIdAsync(oauthAcc.UserId, ct);
            userId = existing.Id;
            userLang = existing.Language;
            userCurrency = existing.Currency;
        }
        catch (NotFoundException)
        {
            User user;
            try
            {
                user = await users.GetByEmailAsync(info.Email, ct);
                // Existing email/password user linking Google for the first
                // time. Google proved ownership of this address — auto-verify.
                await users.MarkVerifiedAsync(user.Id, ct);
            }
            catch (NotFoundException)
            {
                var lang = string.IsNullOrEmpty(request.Language) ? "en" : request.Language;
                var currency = string.IsNullOrEmpty(request.Currency) ? "USD" : request.Currency;
                user = await users.CreateAsync(new User
                {
                    Email = info.Email,
                    FirstName = info.GivenName,
                    LastName = info.FamilyName,
                    Language = lang,
                    Currency = currency,
                }, ct);
                // Google already proved ownership of this email — skip the
                // token/email verification flow entirely.
                await users.MarkVerifiedAsync(user.Id, ct);
                isNew = true;
            }

            userId = user.Id;
            userLang = user.Language;
            userCurrency = user.Currency;

            await users.CreateOAuthAccountAsync(new OAuthAccount
            {
                UserId = userId,
                OauthName = AuthConstants.OauthProviderGoogle,
                AccountId = info.Sub,
                AccountEmail = info.Email,
            }, ct);
        }

        // Google OAuth has no "remember me" UI — always issue the long-lived
        // refresh token a user gets from checking "remember me" on the form.
        var accessToken = jwt.GenerateToken(userId, AuthConstants.AccessTokenLifetime);
        var rawRefreshToken = RefreshTokenGenerator.GenerateRaw();
        await refreshTokens.IssueAsync(userId, request.DeviceId, RefreshTokenGenerator.Hash(rawRefreshToken),
            (int)AuthConstants.RememberMeTokenLifetime.TotalSeconds, DateTime.UtcNow.Add(AuthConstants.RememberMeTokenLifetime), ct);
        logger.LogInformation("auth.google_exchange.success user_id={UserId} device_id={DeviceId} is_new={IsNew}", userId, request.DeviceId, isNew);

        return new ExchangeGoogleCodeResult(accessToken, (long)AuthConstants.AccessTokenLifetime.TotalSeconds, isNew, userLang, userCurrency, rawRefreshToken);
    }
}
