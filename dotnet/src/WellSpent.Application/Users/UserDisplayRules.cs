using WellSpent.Domain.Entities;

namespace WellSpent.Application.Users;

/// <summary>Pure derivation rules mirrored from internal/handler/convert.go and user_handler.go — kept as plain functions so AutoMapper's profile stays a thin wiring layer.</summary>
public static class UserDisplayRules
{
    /// <summary>Set by hand only (migration 000045) to exempt QA/automated-test accounts from the client-side email-verification gate.</summary>
    private const string AccountTypeTest = "test";

    private const string ApplePrivateRelayDomain = "@privaterelay.appleid.com";

    /// <summary>What <c>User.is_verified</c> means to a client: "may this account use the app". A test account reports verified on the wire while its stored IsVerified stays false.</summary>
    public static bool IsVerificationSatisfied(User user) =>
        user.IsVerified || user.AccountType == AccountTypeTest;

    /// <summary>True when the address is an Apple "Hide My Email" alias — meaningless to the user, so clients show "Signed with Apple" instead.</summary>
    public static bool IsApplePrivateEmail(string email) =>
        email.Trim().ToLowerInvariant().EndsWith(ApplePrivateRelayDomain, StringComparison.Ordinal);

    /// <summary>Parses the stored stringified FilingStatus enum value back to an int. Empty/unparseable means unspecified (0).</summary>
    public static int ParseFilingStatus(string? stored) =>
        int.TryParse(stored, out var value) ? value : 0;
}
