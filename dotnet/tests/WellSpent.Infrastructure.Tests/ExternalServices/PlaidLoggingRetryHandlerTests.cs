using Microsoft.Extensions.Logging.Abstractions;
using WellSpent.Infrastructure.Configuration;
using WellSpent.Infrastructure.ExternalServices;

namespace WellSpent.Infrastructure.Tests.ExternalServices;

/// <summary>Mirrors internal/plaid/transport_test.go's TestRoundTrip_* suite — same scenarios, driven through a fake inner HttpMessageHandler instead of a roundTripFunc.</summary>
public sealed class PlaidLoggingRetryHandlerTests
{
    private static AppConfig BuildConfig(int maxRetries = 3, TimeSpan? retryDelay = null) => new()
    {
        DatabaseUrl = "unused",
        JwtSecret = "unused",
        Env = "test",
        PlaidHttpMaxRetries = maxRetries,
        PlaidHttpRetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(1),
    };

    private static HttpClient BuildClient(FakeInnerHandler inner, AppConfig config)
    {
        var retryHandler = new PlaidLoggingRetryHandler(NullLogger<PlaidLoggingRetryHandler>.Instance, config)
        {
            InnerHandler = inner,
        };
        return new HttpClient(retryHandler);
    }

    private static HttpRequestMessage BuildRequest() =>
        new(HttpMethod.Post, "https://sandbox.plaid.com/transactions/sync")
        {
            Content = new StringContent("""{"cursor":""}"""),
        };

    [Fact]
    public async Task SuccessNoRetry()
    {
        var inner = new FakeInnerHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""") });
        using var client = BuildClient(inner, BuildConfig());

        var response = await client.SendAsync(BuildRequest());

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.Calls);
        Assert.Equal("""{"ok":true}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RetriesOn5xxThenSucceeds()
    {
        var inner = new FakeInnerHandler(call => call < 3
            ? new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError) { Content = new StringContent("""{"error":"internal"}""") }
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""") });
        using var client = BuildClient(inner, BuildConfig(maxRetries: 3));

        var response = await client.SendAsync(BuildRequest());

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task NoRetryOn400()
    {
        var inner = new FakeInnerHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new StringContent("""{"error_code":"INVALID_REQUEST"}""") });
        using var client = BuildClient(inner, BuildConfig(maxRetries: 3));

        var response = await client.SendAsync(BuildRequest());

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task RetriesOnNetworkError()
    {
        var inner = new FakeInnerHandler(call => call < 2
            ? throw new HttpRequestException("connection reset")
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""") });
        using var client = BuildClient(inner, BuildConfig(maxRetries: 3));

        var response = await client.SendAsync(BuildRequest());

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task ExhaustsMaxRetries()
    {
        var inner = new FakeInnerHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { Content = new StringContent("""{"error":"unavailable"}""") });
        using var client = BuildClient(inner, BuildConfig(maxRetries: 2));

        var response = await client.SendAsync(BuildRequest());

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls); // 1 initial + 2 retries
    }

    [Fact]
    public async Task NetworkError_ExhaustsRetries_Throws()
    {
        var inner = new FakeInnerHandler(_ => throw new HttpRequestException("connection reset"));
        using var client = BuildClient(inner, BuildConfig(maxRetries: 2));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(BuildRequest()));
        Assert.Equal(3, inner.Calls); // 1 initial + 2 retries
    }

    private sealed class FakeInnerHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(Calls));
        }
    }
}
