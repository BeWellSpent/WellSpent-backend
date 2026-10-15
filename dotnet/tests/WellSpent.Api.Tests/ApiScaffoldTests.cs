using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace WellSpent.Api.Tests;

/// <summary>
/// Pipeline-level smoke tests (DI, JWT middleware, error mapping) using a
/// real authenticated endpoint (GET /rest/v1/users/me) rather than a
/// throwaway debug route, now that one exists (B2). DATABASE_URL points
/// nowhere reachable here on purpose — these tests only need to prove
/// authentication happens before the handler runs, not exercise real
/// business behavior (that's WellSpent.Application.Tests's job, with a
/// mocked repository). A valid token that passes the 401 gate still fails
/// downstream with a 500 (DB unreachable) — asserted as "anything but 401",
/// which is the only claim this layer needs to make.
/// </summary>
public sealed class ApiScaffoldTests : IDisposable
{
    private const string TestJwtSecret = "test-secret-at-least-32-bytes-long-for-hs256";

    private readonly WebApplicationFactory<Program> _factory;

    public ApiScaffoldTests()
    {
        // ENV points at a value with no matching .env.<env> file, so
        // AppConfig.Load() never tries to read real secrets off disk during
        // tests — DATABASE_URL/JWT_SECRET below are the only source.
        Environment.SetEnvironmentVariable("ENV", "test");
        Environment.SetEnvironmentVariable("JWT_SECRET", TestJwtSecret);
        // Only needs to be a syntactically valid DATABASE_URL — nothing here
        // needs it to actually connect. Same postgresql:// URI shape as
        // every real .env file uses (see PostgresConnectionString).
        Environment.SetEnvironmentVariable("DATABASE_URL", "postgresql://user:pass@localhost:5432/db?sslmode=disable");

        _factory = new WebApplicationFactory<Program>();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UsersMe_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/rest/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UsersMe_WithValidToken_PassesAuthentication()
    {
        var client = _factory.CreateClient();
        var token = IssueToken(Guid.NewGuid().ToString(), TestJwtSecret);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/rest/v1/users/me");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UsersMe_WithTokenSignedByDifferentSecret_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var token = IssueToken(Guid.NewGuid().ToString(), "a-completely-different-secret-value-xx");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/rest/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Mints a token the same shape as internal/auth/jwt.go's
    // GenerateTokenWithLifetime: HS256, a "sub" claim carrying the user id,
    // standard exp/iat claims. Proves the API's JWT middleware accepts a
    // token shaped like the one the Go backend issues.
    private static string IssueToken(string userId, string secret)
    {
        var handler = new JsonWebTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["sub"] = userId },
            Expires = DateTime.UtcNow.AddMinutes(5),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        return handler.CreateToken(descriptor);
    }

    public void Dispose() => _factory.Dispose();
}
