using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>Pure half of transport.go's logging/redaction, split out of <see cref="PlaidLoggingRetryHandler"/> for direct unit testing.</summary>
public static class PlaidHttpLogFormatting
{
    private const int MaxLoggedBodyBytes = 4096;

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plaid-Client-Id", "Plaid-Secret",
    };

    private static readonly HashSet<string> SensitiveJsonFields = new()
    {
        "client_id", "secret", "access_token", "public_token", "link_token",
    };

    public static bool ShouldRetry(int status) => status == (int)HttpStatusCode.TooManyRequests || status >= 500;

    public static Dictionary<string, string> RedactHeaders(HttpHeaders headers, HttpContentHeaders? contentHeaders = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            result[header.Key] = SensitiveHeaders.Contains(header.Key) ? "REDACTED" : string.Join(",", header.Value);
        }

        if (contentHeaders is not null)
        {
            foreach (var header in contentHeaders)
            {
                result[header.Key] = SensitiveHeaders.Contains(header.Key) ? "REDACTED" : string.Join(",", header.Value);
            }
        }

        return result;
    }

    public static string FormatBody(byte[] body, bool redactSensitive)
    {
        var outBytes = body;
        if (redactSensitive)
        {
            try
            {
                var node = JsonNode.Parse(body);
                if (node is not null)
                {
                    RedactValue(node);
                    outBytes = Encoding.UTF8.GetBytes(node.ToJsonString());
                }
            }
            catch (JsonException)
            {
                // Not valid JSON — fall through and log the raw (still-truncated) bytes.
            }
        }

        if (outBytes.Length > MaxLoggedBodyBytes)
        {
            return Encoding.UTF8.GetString(outBytes, 0, MaxLoggedBodyBytes) + "...(truncated)";
        }

        return Encoding.UTF8.GetString(outBytes);
    }

    private static void RedactValue(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (SensitiveJsonFields.Contains(key))
                    {
                        obj[key] = "REDACTED";
                    }
                    else
                    {
                        RedactValue(obj[key]);
                    }
                }

                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    RedactValue(item);
                }

                break;
        }
    }
}
