using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Auth;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Users.DeleteMe;

public sealed record DeleteMeCommand(Guid UserId) : IRequest<Unit>;

public sealed class DeleteMeCommandHandler(
    IUserRepository users,
    IAppleAuthClient apple,
    ICryptoService crypto,
    IOptions<AuthOptions> options,
    ILogger<DeleteMeCommandHandler> logger) : IRequestHandler<DeleteMeCommand, Unit>
{
    public async Task<Unit> Handle(DeleteMeCommand request, CancellationToken ct)
    {
        await RevokeAppleTokensAsync(request.UserId, ct);
        await users.SoftDeleteAsync(request.UserId, ct);
        return Unit.Value;
    }

    // Tells Apple to invalidate the user's credentials for this app, which
    // App Store Review 5.1.1(v) requires of any app offering Sign in with
    // Apple. Every failure is logged and swallowed: a user who asked to
    // delete their account must end up deleted regardless of whether Apple's
    // endpoint was reachable.
    private async Task RevokeAppleTokensAsync(Guid userId, CancellationToken ct)
    {
        List<Domain.Entities.OAuthAccount> accounts;
        try
        {
            accounts = await users.ListOAuthAccountsByUserAsync(userId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "user.delete.list_oauth_accounts_failed user_id={UserId}", userId);
            return;
        }

        foreach (var acc in accounts)
        {
            if (acc.OauthName != AuthConstants.OauthProviderApple || string.IsNullOrEmpty(acc.RefreshToken))
            {
                continue;
            }
            if (string.IsNullOrEmpty(options.Value.EncryptionKey))
            {
                logger.LogError("user.delete.apple_revoke_skipped: ENCRYPTION_KEY unset user_id={UserId}", userId);
                return;
            }

            string refreshToken;
            try
            {
                refreshToken = crypto.Decrypt(acc.RefreshToken, options.Value.EncryptionKey);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "user.delete.apple_refresh_token_decrypt_failed user_id={UserId}", userId);
                continue;
            }

            try
            {
                await apple.RevokeRefreshTokenAsync(refreshToken, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "user.delete.apple_revoke_failed user_id={UserId}", userId);
                continue;
            }

            logger.LogInformation("user.delete.apple_revoked user_id={UserId}", userId);
        }
    }
}
