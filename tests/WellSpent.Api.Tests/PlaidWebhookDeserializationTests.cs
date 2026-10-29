using System.Text.Json;
using System.Text.Json.Serialization;
using Going.Plaid;
using Going.Plaid.Converters;
using Going.Plaid.Webhook;
using Xunit;

namespace WellSpent.Api.Tests;

/// <summary>
/// Regression test for a real production 500: WebhookBase's WebhookType/
/// WebhookCode/Environment are enums with no [JsonConverter] of their own,
/// and Plaid sends them as lowercase strings ("production"), not enum
/// member names ("Production"). Without JsonStringEnumConverter in the
/// options WebhookEndpoints builds, every real delivery failed to
/// deserialize before WebhookBaseConverter could even pick a subtype.
/// Mirrors the exact options construction in WebhookEndpoints.HandlePlaidWebhookAsync.
/// </summary>
public sealed class PlaidWebhookDeserializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(), new WebhookBaseConverter() },
    };

    [Fact]
    public void SyncUpdatesAvailable_WithLowercaseEnvironment_Deserializes()
    {
        var json = """
            {"webhook_type":"TRANSACTIONS","webhook_code":"SYNC_UPDATES_AVAILABLE","item_id":"abc123","environment":"production"}
            """;

        var payload = JsonSerializer.Deserialize<WebhookBase>(json, Options);

        Assert.IsType<SyncUpdatesAvailableWebhook>(payload);
    }

    [Fact]
    public void ItemError_WithLowercaseEnvironment_Deserializes()
    {
        // No nested "error" — PlaidErrorType's members don't match real Plaid error_type values, a separate gap not exercised here.
        var json = """
            {"webhook_type":"ITEM","webhook_code":"ERROR","item_id":"abc123","environment":"sandbox"}
            """;

        var payload = JsonSerializer.Deserialize<WebhookBase>(json, Options);

        Assert.IsType<ItemErrorWebhook>(payload);
    }
}
