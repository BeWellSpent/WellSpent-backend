using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Auth.AppleSignIn;

public sealed record SignInWithAppleCommand(
    string IdentityToken,
    string AuthorizationCode,
    string FirstName, // Apple supplies the name ONLY on the very first authorization, never again — written at creation time and never overwritten
    string LastName,
    string Language,
    string Currency) : IRequest<SignInWithAppleResult>;

public sealed record SignInWithAppleResult(string AccessToken, long ExpiresIn, bool IsNewUser, string Language, string Currency);

/// <summary>
/// Resolves a native Sign in with Apple credential to a session. Account
/// resolution mirrors ExchangeGoogleCodeCommandHandler: look the user up by
/// the provider's stable subject first, fall back to matching an existing
/// account by email, and only create a new user when neither hits — the
/// email fallback is what makes "already signed up with Google, same
/// address" land on the existing account instead of a duplicate.
/// </summary>
public sealed class SignInWithAppleCommandHandler(
    IUserRepository users,
    IAppleAuthClient apple,
    ICryptoService crypto,
    IJwtService jwt,
    IOptions<AuthOptions> options,
    ILogger<SignInWithAppleCommandHandler> logger) : IRequestHandler<SignInWithAppleCommand, SignInWithAppleResult>
{
    public async Task<SignInWithAppleResult> Handle(SignInWithAppleCommand request, CancellationToken ct)
    {
        AppleIdentity identity;
        try
        {
            identity = await apple.VerifyIdentityTokenAsync(request.IdentityToken, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "auth.apple.verify_failed");
            throw new AppValidationException("invalid Apple identity token");
        }
        if (string.IsNullOrEmpty(identity.Email))
        {
            throw new AppValidationException("Apple identity token contained no email address");
        }

        Guid userId, oauthId;
        string userLang, userCurrency;
        var isNew = false;

        try
        {
            var oauthAcc = await users.GetOAuthAccountAsync(AuthConstants.OauthProviderApple, identity.Sub, ct);
            // Returning user — resolved by Apple's stable subject.
            var existing = await users.GetByIdAsync(oauthAcc.UserId, ct);
            if (!existing.IsActive)
            {
                throw new ForbiddenException("account is inactive");
            }
            userId = existing.Id;
            userLang = existing.Language;
            userCurrency = existing.Currency;
            oauthId = oauthAcc.Id;
        }
        catch (NotFoundException)
        {
            User user;
            try
            {
                user = await users.GetByEmailAsync(identity.Email, ct);
                if (!identity.EmailVerified)
                {
                    // Linking to a pre-existing account on the strength of an
                    // unverified email claim would let anyone who can mint
                    // such a claim take over that account. Creating a fresh
                    // account is safe; adopting someone else's is not.
                    logger.LogWarning("auth.apple.unverified_email_link_rejected email={Email}", identity.Email);
                    throw new AppValidationException("Apple did not confirm this email address");
                }
                if (!user.IsActive)
                {
                    throw new ForbiddenException("account is inactive");
                }
            }
            catch (NotFoundException)
            {
                var lang = string.IsNullOrEmpty(request.Language) ? "en" : request.Language;
                var currency = string.IsNullOrEmpty(request.Currency) ? "USD" : request.Currency;
                user = await users.CreateAsync(new User
                {
                    Email = identity.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Language = lang,
                    Currency = currency,
                }, ct);
                isNew = true;
            }

            // Apple vouched for this address, so skip the email-verification
            // round trip — same reasoning as the Google path.
            await users.MarkVerifiedAsync(user.Id, ct);

            var created = await users.CreateOAuthAccountAsync(new OAuthAccount
            {
                UserId = user.Id,
                OauthName = AuthConstants.OauthProviderApple,
                AccountId = identity.Sub,
                AccountEmail = identity.Email,
            }, ct);

            userId = user.Id;
            userLang = user.Language;
            userCurrency = user.Currency;
            oauthId = created.Id;
        }

        await StoreAppleRefreshTokenAsync(oauthId, request.AuthorizationCode, ct);

        // No "remember me" control exists on either OAuth flow, so both issue
        // the long-lived token a user would get by ticking it on the login form.
        var token = jwt.GenerateToken(userId, AuthConstants.RememberMeTokenLifetime);
        return new SignInWithAppleResult(token, (long)AuthConstants.RememberMeTokenLifetime.TotalSeconds, isNew, userLang, userCurrency);
    }

    // Exchanges the one-time authorization code for a refresh token and
    // persists it encrypted, so the account can be revoked with Apple on
    // deletion. Deliberately best-effort and never throws: failing a sign-in
    // because Apple's token endpoint was briefly unavailable is far worse
    // than a missing revocation token, and the next sign-in supplies a fresh
    // code to retry with.
    private async Task StoreAppleRefreshTokenAsync(Guid oauthAccountId, string authorizationCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            return;
        }

        string refreshToken;
        try
        {
            refreshToken = await apple.ExchangeCodeAsync(authorizationCode, ct);
        }
        catch (AppleKeyNotConfiguredException)
        {
            logger.LogWarning("auth.apple.exchange_skipped: APPLE_KEY_ID/APPLE_PRIVATE_KEY unset, account cannot be revoked with Apple on deletion");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "auth.apple.exchange_failed");
            return;
        }

        if (string.IsNullOrEmpty(options.Value.EncryptionKey))
        {
            logger.LogError("auth.apple.refresh_token_not_stored: ENCRYPTION_KEY unset");
            return;
        }

        string encrypted;
        try
        {
            encrypted = crypto.Encrypt(refreshToken, options.Value.EncryptionKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "auth.apple.encrypt_refresh_token_failed");
            return;
        }

        try
        {
            await users.UpdateOAuthAccountRefreshTokenAsync(oauthAccountId, encrypted, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "auth.apple.store_refresh_token_failed");
        }
    }
}
