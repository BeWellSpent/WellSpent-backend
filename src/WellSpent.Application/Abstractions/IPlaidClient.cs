namespace WellSpent.Application.Abstractions;

/// <summary>A Plaid-linked bank account normalised for WellSpent.</summary>
public sealed record PlaidLinkedAccount(string PlaidAccountId, string Name, string Mask, string Type, string Subtype);

/// <summary>A Plaid transaction normalised for import. PendingTransactionId links a settled transaction back to the pending one it replaces; empty otherwise.</summary>
public sealed record PlaidImportedTransaction(
    string PlaidId,
    string AccountId,
    string Name,
    decimal Amount,
    DateOnly Date,
    string PfcPrimary,
    string PfcDetailed,
    string ReferenceNumber,
    string PpdId,
    string PendingTransactionId);

public sealed record PlaidLinkToken(string LinkToken, string Expiration);

public sealed record PlaidExchangedToken(string AccessToken, string ItemId);

public sealed record PlaidAccountsResult(List<PlaidLinkedAccount> Accounts, string InstitutionId);

public sealed record PlaidSyncResult(
    List<PlaidImportedTransaction> Added,
    List<PlaidImportedTransaction> Modified,
    List<string> RemovedIds,
    string NextCursor);

/// <summary>An EC JWK Plaid returns for webhook signature verification. X/Y are base64url-encoded curve coordinates; ExpiredAt non-null means this key was rotated out and must not be trusted.</summary>
public sealed record PlaidWebhookVerificationKey(string Kid, string Crv, string X, string Y, int? ExpiredAt);

/// <summary>Mirrors internal/plaid/client.go's Client interface — the seam that lets Plaid-dependent logic be unit-tested without reaching Plaid.</summary>
public interface IPlaidClient
{
    /// <summary>Pass a non-empty updateAccessToken to request update mode (add/remove accounts) for an existing item instead of a fresh connect flow.</summary>
    Task<PlaidLinkToken> CreateLinkTokenAsync(string userId, string updateAccessToken, string redirectUri, CancellationToken ct);

    Task<PlaidExchangedToken> ExchangePublicTokenAsync(string publicToken, CancellationToken ct);

    Task<string> GetInstitutionNameAsync(string institutionId, CancellationToken ct);

    Task RemoveItemAsync(string accessToken, CancellationToken ct);

    Task<PlaidAccountsResult> GetAccountsAsync(string accessToken, CancellationToken ct);

    /// <summary>Fetches incremental transaction changes since cursor. Pass an empty cursor for the initial sync.</summary>
    Task<PlaidSyncResult> SyncTransactionsAsync(string accessToken, string cursor, CancellationToken ct);

    /// <summary>Fetches the EC public key identified by a webhook JWT's "kid" header, for PlaidWebhookVerifier to verify the signature against.</summary>
    Task<PlaidWebhookVerificationKey> GetWebhookVerificationKeyAsync(string keyId, CancellationToken ct);

    /// <summary>Registers (or replaces) the webhook URL Plaid calls for an already-linked item — only newly-created link tokens pick up PlaidWebhookUrl automatically, so this is how an existing item catches up (see the PlaidSync job's --backfill-webhooks mode).</summary>
    Task UpdateItemWebhookAsync(string accessToken, string webhookUrl, CancellationToken ct);
}
