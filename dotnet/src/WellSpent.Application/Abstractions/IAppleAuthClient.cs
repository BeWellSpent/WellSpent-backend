namespace WellSpent.Application.Abstractions;

public sealed record AppleIdentity(string Sub, string Email, bool EmailVerified, bool IsPrivateEmail);

/// <summary>
/// Thrown by <see cref="IAppleAuthClient.ExchangeCodeAsync"/>/<see cref="IAppleAuthClient.RevokeRefreshTokenAsync"/>
/// when APPLE_KEY_ID/APPLE_PRIVATE_KEY are unset, mirroring Go's
/// ErrAppleKeyNotConfigured sentinel. Callers treat this as a degradation to
/// log, never a failure to surface — identity-token verification (sign-in
/// itself) needs no private key, only the code exchange and revocation do.
/// </summary>
public sealed class AppleKeyNotConfiguredException : Exception;

/// <summary>Mirrors internal/auth/apple.go's AppleAuthenticator — the seam that lets account resolution be unit-tested without reaching Apple.</summary>
public interface IAppleAuthClient
{
    Task<AppleIdentity> VerifyIdentityTokenAsync(string identityToken, CancellationToken ct);

    /// <summary>Trades a one-time authorization code for a refresh token. Throws <see cref="AppleKeyNotConfiguredException"/> when no signing key is configured.</summary>
    Task<string> ExchangeCodeAsync(string code, CancellationToken ct);

    Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct);
}
