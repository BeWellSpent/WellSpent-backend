namespace WellSpent.Application.Abstractions;

public sealed record GoogleUserInfo(string Sub, string Email, string GivenName, string FamilyName);

/// <summary>Mirrors internal/auth/oauth.go's GoogleOAuth.</summary>
public interface IGoogleOAuthClient
{
    string GetAuthUrl(string state);
    Task<GoogleUserInfo> ExchangeCodeAsync(string code, CancellationToken ct);
}
