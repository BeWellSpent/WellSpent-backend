using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WellSpent.Infrastructure.ExternalServices;

namespace WellSpent.Infrastructure.Tests.ExternalServices;

/// <summary>Mirrors internal/plaid/transport_test.go's TestRedactHeaders/TestFormatBody_* suite.</summary>
public sealed class PlaidHttpLogFormattingTests
{
    [Theory]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(200, false)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    public void ShouldRetry_MatchesNetworkErrorOr429Or5xxOnly(int status, bool expected)
    {
        Assert.Equal(expected, PlaidHttpLogFormatting.ShouldRetry(status));
    }

    [Fact]
    public void RedactHeaders_RedactsClientIdAndSecret_PreservesOthers()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://sandbox.plaid.com/transactions/sync")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("PLAID-CLIENT-ID", "client-123");
        request.Headers.TryAddWithoutValidation("PLAID-SECRET", "shh-secret");

        var result = PlaidHttpLogFormatting.RedactHeaders(request.Headers, request.Content.Headers);

        Assert.Equal("REDACTED", result["Plaid-Client-Id"]);
        Assert.Equal("REDACTED", result["Plaid-Secret"]);
        Assert.Contains("application/json", result["Content-Type"]);
    }

    [Fact]
    public void FormatBody_RedactsSensitiveFields_PreservesOthers_IncludingNested()
    {
        var body = Encoding.UTF8.GetBytes(
            """{"client_id":"abc","secret":"xyz","access_token":"tok","cursor":"c1","nested":{"public_token":"pt"}}""");

        var result = PlaidHttpLogFormatting.FormatBody(body, redactSensitive: true);

        using var parsed = JsonDocument.Parse(result);
        var root = parsed.RootElement;
        Assert.Equal("REDACTED", root.GetProperty("client_id").GetString());
        Assert.Equal("REDACTED", root.GetProperty("secret").GetString());
        Assert.Equal("REDACTED", root.GetProperty("access_token").GetString());
        Assert.Equal("c1", root.GetProperty("cursor").GetString());
        Assert.Equal("REDACTED", root.GetProperty("nested").GetProperty("public_token").GetString());
    }

    [Fact]
    public void FormatBody_NoRedactWhenDisabled()
    {
        var body = Encoding.UTF8.GetBytes("""{"secret":"xyz","cursor":"c1"}""");

        var result = PlaidHttpLogFormatting.FormatBody(body, redactSensitive: false);

        Assert.Contains("\"xyz\"", result);
    }

    [Fact]
    public void FormatBody_Truncates()
    {
        var body = Encoding.UTF8.GetBytes(new string('a', 4096 + 500));

        var result = PlaidHttpLogFormatting.FormatBody(body, redactSensitive: false);

        Assert.EndsWith("...(truncated)", result);
        Assert.True(result.Length <= 4096 + "...(truncated)".Length);
    }

    [Fact]
    public void FormatBody_NonJsonDoesNotThrow()
    {
        var result = PlaidHttpLogFormatting.FormatBody(Encoding.UTF8.GetBytes("not json"), redactSensitive: true);

        Assert.Equal("not json", result);
    }
}
