namespace WellSpent.Domain.Entities;

/// <summary>
/// Maps 1:1 to the existing `users` table (see docs/specs/03-data-model.md).
/// Column shapes are preserved exactly as the Go backend already uses them —
/// notably <see cref="FilingStatus"/>, stored as the stringified integer value
/// of the proto FilingStatus enum, not a descriptive string. Both backends
/// read/write the same rows during the strangler-fig cutover, so changing that
/// representation here would silently break the Go side.
/// </summary>
public sealed class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public string? HashedPassword { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSuperuser { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; }

    public string? CountryCode { get; set; }
    public string? StateCode { get; set; }

    /// <summary>Stringified <c>FilingStatus</c> proto enum value, e.g. "1". NOT NULL DEFAULT '' in the schema — empty means unspecified.</summary>
    public string FilingStatus { get; set; } = "";

    /// <summary>The <c>TaxPaymentFrequency</c> proto enum value directly (1/3/4/6/12 = months), 0 = unspecified.</summary>
    public int TaxPaymentFrequency { get; set; }

    public string Language { get; set; } = "en";
    public string Currency { get; set; } = "USD";

    public Guid? EmailVerificationToken { get; set; }
    public DateTime? EmailVerificationExpiresAt { get; set; }
    public DateTime? EmailVerificationLastSentAt { get; set; }

    /// <summary>"active" | "blocked" | "disabled".</summary>
    public string Status { get; set; } = "active";
    public DateTime? ActiveUntil { get; set; }

    /// <summary>"free" | "pro" | "lifetime".</summary>
    public string Plan { get; set; } = "free";

    /// <summary>"standard" | "test" — "test" exempts the account from the client-side email-verification gate.</summary>
    public string AccountType { get; set; } = "standard";

    public List<OAuthAccount> OAuthAccounts { get; set; } = [];
}
