using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using WellSpent.Infrastructure.Configuration;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>
/// Mirrors internal/plaid/transport.go's NewLoggingRetryTransport exactly:
/// structured request/response logging (body capped at 4096 bytes, via
/// <see cref="PlaidHttpLogFormatting"/>), credential redaction
/// (Plaid-Client-Id/Plaid-Secret headers always; client_id/secret/
/// access_token/public_token/link_token body fields when configured), and
/// retry on network error/429/5xx only — never on a 4xx, which would just
/// fail identically and burn API quota. Registered on the named "PlaidClient"
/// HttpClient that Going.Plaid's PlaidClient dispatches through internally.
/// </summary>
public sealed class PlaidLoggingRetryHandler(ILogger<PlaidLoggingRetryHandler> logger, AppConfig config) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? bodyBytes = null;
        HttpContentHeaders? contentHeaders = null;
        if (request.Content is not null)
        {
            bodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentHeaders = request.Content.Headers;
        }

        for (var attempt = 0; attempt <= config.PlaidHttpMaxRetries; attempt++)
        {
            using var attemptRequest = CloneRequest(request, bodyBytes, contentHeaders);
            LogRequest(attemptRequest, bodyBytes, attempt);

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "plaid.http.network_error url={Url} attempt={Attempt}", request.RequestUri, attempt + 1);
                if (attempt == config.PlaidHttpMaxRetries)
                {
                    throw;
                }

                await Task.Delay(config.PlaidHttpRetryDelay, cancellationToken);
                continue;
            }

            await LogResponseAsync(attemptRequest, response, attempt, cancellationToken);

            if (!PlaidHttpLogFormatting.ShouldRetry((int)response.StatusCode) || attempt == config.PlaidHttpMaxRetries)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(config.PlaidHttpRetryDelay, cancellationToken);
        }

        // Unreachable: the loop above always returns or throws on its final iteration.
        throw new InvalidOperationException("plaid: retry loop exhausted with no response or exception");
    }

    // .NET HttpRequestMessage instances can only be sent once, so each retry
    // attempt needs its own clone built from the buffered body/headers.
    private static HttpRequestMessage CloneRequest(HttpRequestMessage original, byte[]? bodyBytes, HttpContentHeaders? contentHeaders)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (bodyBytes is not null)
        {
            var content = new ByteArrayContent(bodyBytes);
            if (contentHeaders is not null)
            {
                foreach (var header in contentHeaders)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            clone.Content = content;
        }

        return clone;
    }

    private void LogRequest(HttpRequestMessage request, byte[]? body, int attempt)
    {
        var headers = PlaidHttpLogFormatting.RedactHeaders(request.Headers, request.Content?.Headers);
        if (body is { Length: > 0 })
        {
            logger.LogInformation(
                "plaid.http.request method={Method} url={Url} attempt={Attempt} headers={@Headers} body={Body}",
                request.Method, request.RequestUri, attempt + 1, headers, PlaidHttpLogFormatting.FormatBody(body, config.PlaidLogRedactSensitive));
        }
        else
        {
            logger.LogInformation(
                "plaid.http.request method={Method} url={Url} attempt={Attempt} headers={@Headers}",
                request.Method, request.RequestUri, attempt + 1, headers);
        }
    }

    private async Task LogResponseAsync(HttpRequestMessage request, HttpResponseMessage response, int attempt, CancellationToken ct)
    {
        if (response.Content is null)
        {
            logger.LogInformation("plaid.http.response url={Url} status={Status} attempt={Attempt}",
                request.RequestUri, (int)response.StatusCode, attempt + 1);
            return;
        }

        var originalHeaders = response.Content.Headers;
        var body = await response.Content.ReadAsByteArrayAsync(ct);

        // Re-attach so the caller (Going.Plaid's response parser) can still read the body.
        var replacement = new ByteArrayContent(body);
        foreach (var header in originalHeaders)
        {
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = replacement;

        if (body.Length > 0)
        {
            logger.LogInformation("plaid.http.response url={Url} status={Status} attempt={Attempt} body={Body}",
                request.RequestUri, (int)response.StatusCode, attempt + 1, PlaidHttpLogFormatting.FormatBody(body, config.PlaidLogRedactSensitive));
        }
        else
        {
            logger.LogInformation("plaid.http.response url={Url} status={Status} attempt={Attempt}",
                request.RequestUri, (int)response.StatusCode, attempt + 1);
        }
    }
}
