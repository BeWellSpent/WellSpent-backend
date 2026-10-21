using System.Security.Cryptography;
using System.Text;

namespace WellSpent.Application.Common;

/// <summary>
/// Opaque session refresh tokens: 32 random bytes, base64url-encoded; stored
/// as a SHA-256 hex digest, never raw. Unrelated to Apple's own OAuth refresh
/// token (see SignInWithAppleCommand's StoreAppleRefreshTokenAsync).
/// </summary>
public static class RefreshTokenGenerator
{
    public static string GenerateRaw() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
