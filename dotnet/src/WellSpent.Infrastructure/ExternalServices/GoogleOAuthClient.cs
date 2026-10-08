using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WellSpent.Application.Abstractions;
using WellSpent.Infrastructure.Configuration;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>Mirrors internal/auth/oauth.go's GoogleOAuth: a hand-rolled authorization-code flow (no golang.org/x/oauth2-equivalent library pulled in — the flow is three HTTP calls and not worth a dependency).</summary>
public sealed class GoogleOAuthClient(HttpClient http, AppConfig config) : IGoogleOAuthClient
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";

    public string GetAuthUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = config.GoogleClientId,
            ["redirect_uri"] = config.GoogleRedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["access_type"] = "offline",
        };
        var queryString = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{AuthEndpoint}?{queryString}";
    }

    public async Task<GoogleUserInfo> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var tokenResponse = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = config.GoogleClientId,
            ["client_secret"] = config.GoogleClientSecret,
            ["code"] = code,
            ["redirect_uri"] = config.GoogleRedirectUri,
            ["grant_type"] = "authorization_code",
        }), ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("google oauth: empty token response");

        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        var userInfoResponse = await http.SendAsync(request, ct);
        userInfoResponse.EnsureSuccessStatusCode();
        var info = await userInfoResponse.Content.ReadFromJsonAsync<GoogleUserInfoResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("google oauth: empty userinfo response");

        return new GoogleUserInfo(info.Sub, info.Email, info.GivenName, info.FamilyName);
    }

    private sealed record GoogleTokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record GoogleUserInfoResponse(
        string Sub,
        string Email,
        [property: JsonPropertyName("given_name")] string GivenName,
        [property: JsonPropertyName("family_name")] string FamilyName);
}
