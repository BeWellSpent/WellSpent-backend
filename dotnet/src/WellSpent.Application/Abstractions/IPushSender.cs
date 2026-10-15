namespace WellSpent.Application.Abstractions;

/// <summary>Mirrors the APNs send in internal/service/notification_service.go's sendPush.</summary>
public interface IPushSender
{
    /// <summary>Sends an alert push to one device. Never throws — failures are the caller's to log.</summary>
    Task<bool> SendAsync(string deviceToken, string title, string body, CancellationToken ct);
}
