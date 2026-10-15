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

    /// <summary>
    /// Renders a user's name for other people to read, falling back to their
    /// email when they haven't set one. Mirrors Go's userDisplayName exactly
    /// — note this is deliberately more forgiving than
    /// Invites.InviteDisplay.InviterName's SQL-COALESCE semantics (a single
    /// present name part is still used here, not discarded).
    /// </summary>
    public static string DisplayName(User user)
    {
        var parts = new List<string>();
        if (user.FirstName is not null) parts.Add(user.FirstName);
        if (user.LastName is not null) parts.Add(user.LastName);
        var name = string.Join(" ", parts).Trim();
        return name.Length > 0 ? name : user.Email;
    }
}
