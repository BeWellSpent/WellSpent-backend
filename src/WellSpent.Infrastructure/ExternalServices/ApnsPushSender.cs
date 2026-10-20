using CorePush.Apple;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;
using WellSpent.Infrastructure.Configuration;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>
/// Mirrors Go's sendPush: JWT provider-token auth (not certificate-based) via
/// the same APNS_AUTH_KEY .p8 contents apns2 reads. One ApnSender per process
/// is fine — CorePush caches the signed provider token internally the same
/// way apns2's token client does, so this isn't re-signing per send.
/// </summary>
public sealed class ApnsPushSender : IPushSender
{
    private readonly ApnSender _sender;
    private readonly ILogger<ApnsPushSender> _logger;

    public ApnsPushSender(AppConfig config, HttpClient httpClient, ILogger<ApnsPushSender> logger)
    {
        _logger = logger;
        var settings = new ApnSettings
        {
            P8PrivateKey = config.ApnsAuthKey,
            P8PrivateKeyId = config.ApnsKeyId,
            TeamId = config.ApnsTeamId,
            AppBundleIdentifier = config.ApnsBundleId,
            ServerType = config.ApnsEnvironment == "production" ? ApnServerType.Production : ApnServerType.Development,
        };
        _sender = new ApnSender(settings, httpClient);
    }

    public async Task<bool> SendAsync(string deviceToken, string title, string body, string alertType, Guid budgetProfileId, CancellationToken ct)
    {
        var payload = new
        {
            aps = new { alert = new { title, body }, badge = 1 },
            alertType,
            budgetProfileId = budgetProfileId.ToString(),
        };
        try
        {
            var result = await _sender.SendAsync(payload, deviceToken, null, 10, 0, ApnPushType.Alert, ct);
            if (!result.IsSuccessStatusCode)
            {
                _logger.LogWarning("notification.send_push_not_sent token={Token} status={StatusCode} error={Error}",
                    deviceToken, result.StatusCode, result.Error);
            }
            return result.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "notification.send_push_failed token={Token}", deviceToken);
            return false;
        }
    }
}

/// <summary>Registered in place of ApnsPushSender when APNS_AUTH_KEY is unset — mirrors Go's "skipped: APNS_AUTH_KEY not set" warning-and-no-op rather than failing the caller.</summary>
public sealed class NoOpPushSender(ILogger<NoOpPushSender> logger) : IPushSender
{
    public Task<bool> SendAsync(string deviceToken, string title, string body, string alertType, Guid budgetProfileId, CancellationToken ct)
    {
        logger.LogWarning("notification.send_push_skipped: APNS_AUTH_KEY not set token={Token}", deviceToken);
        return Task.FromResult(false);
    }
}
