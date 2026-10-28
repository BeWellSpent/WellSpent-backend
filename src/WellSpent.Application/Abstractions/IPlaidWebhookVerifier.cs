namespace WellSpent.Application.Abstractions;

/// <summary>Verifies Plaid's "Plaid-Verification" webhook header against the raw request body. See PlaidWebhookVerifier for the scheme.</summary>
public interface IPlaidWebhookVerifier
{
    Task<bool> VerifyAsync(string verificationHeader, byte[] rawBody, CancellationToken ct);
}
