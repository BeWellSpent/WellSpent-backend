namespace WellSpent.Domain.Entities;

/// <summary>
/// Maps 1:1 to the existing `oauth_account` table. <c>RefreshToken</c> is
/// Apple-only, AES-256-GCM encrypted at rest (see
/// Infrastructure.Security.AesCryptoService) — needed to revoke the user's
/// Apple credentials on account deletion (App Store Review 5.1.1(v)).
/// </summary>
public sealed class OAuthAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>"google" | "apple".</summary>
    public required string OauthName { get; set; }

    /// <summary>The provider's stable subject for this user.</summary>
    public required string AccountId { get; set; }
    public required string AccountEmail { get; set; }
    public string? RefreshToken { get; set; }
}
