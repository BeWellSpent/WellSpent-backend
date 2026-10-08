namespace WellSpent.Application.Notifications;

/// <summary>Mirrors internal/service/notification_service.go's free-tier gating constants. Note: Go does NOT validate AlertType/Channel/ThresholdScope against a fixed set anywhere — mirrored faithfully, not "fixed".</summary>
public static class NotificationConstants
{
    public const string AlertTypeNewTransaction = "new_transaction";
    public const string AlertTypeSpendingThreshold = "spending_threshold";

    public const int DefaultListLimit = 50;
    public const int FreeTierMaxSubscriptionsPerBudget = 2;
}
