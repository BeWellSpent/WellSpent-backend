using Going.Plaid.Accounts;
using Going.Plaid.Entity;
using Going.Plaid.Institutions;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Going.Plaid.Transactions;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;

namespace WellSpent.Infrastructure.ExternalServices;

/// <summary>
/// Wraps Going.Plaid's PlaidClient, mirroring internal/plaid/client.go's
/// *client exactly — same 6 operations, same SyncTransactions pagination and
/// mutation-during-pagination restart behavior. The retry/redaction logging
/// Go attaches via a custom http.RoundTripper is instead a DelegatingHandler
/// (<see cref="PlaidLoggingRetryHandler"/>) registered on the named
/// "PlaidClient" HttpClient that Going.Plaid dispatches through internally.
/// </summary>
public sealed class GoingPlaidClient(Going.Plaid.PlaidClient plaid, ILogger<GoingPlaidClient> logger) : IPlaidClient
{
    // Plaid's documented maximum for /transactions/sync `count` (default is 100 if unset).
    private const int MaxSyncPageSize = 500;

    // Bounds how many times a full paginated sync restarts after
    // TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION, Plaid's documented recovery for that error.
    private const int MaxSyncPaginationRestarts = 3;

    private const string MutationDuringPagination = "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION";

    public async Task<PlaidLinkToken> CreateLinkTokenAsync(string userId, string updateAccessToken, string redirectUri, CancellationToken ct)
    {
        var request = new LinkTokenCreateRequest
        {
            ClientName = "WellSpent",
            Language = Language.English,
            CountryCodes = [CountryCode.Us],
            User = new LinkTokenCreateRequestUser { ClientUserId = userId },
            Products = [Products.Transactions],
        };

        if (!string.IsNullOrEmpty(updateAccessToken))
        {
            request.AccessToken = updateAccessToken;
            request.Update = new LinkTokenCreateRequestUpdate { AccountSelectionEnabled = true };
        }

        // Only native clients (iOS) need this — OAuth institutions must hand
        // control back to the app via a Plaid-dashboard-registered redirect URI.
        if (!string.IsNullOrEmpty(redirectUri))
        {
            request.RedirectUri = redirectUri;
        }

        var response = await plaid.LinkTokenCreateAsync(request);
        ThrowIfError(response.Error, response.IsSuccessStatusCode, "create link token");
        return new PlaidLinkToken(response.LinkToken, response.Expiration.ToString("yyyy-MM-ddTHH:mm:sszzz"));
    }

    public async Task<PlaidExchangedToken> ExchangePublicTokenAsync(string publicToken, CancellationToken ct)
    {
        var response = await plaid.ItemPublicTokenExchangeAsync(new ItemPublicTokenExchangeRequest { PublicToken = publicToken });
        ThrowIfError(response.Error, response.IsSuccessStatusCode, "exchange public token");
        return new PlaidExchangedToken(response.AccessToken, response.ItemId);
    }

    public async Task<string> GetInstitutionNameAsync(string institutionId, CancellationToken ct)
    {
        var response = await plaid.InstitutionsGetByIdAsync(new InstitutionsGetByIdRequest
        {
            InstitutionId = institutionId,
            CountryCodes = [CountryCode.Us],
        });
        ThrowIfError(response.Error, response.IsSuccessStatusCode, "get institution");
        return response.Institution.Name;
    }

    public async Task RemoveItemAsync(string accessToken, CancellationToken ct)
    {
        var response = await plaid.ItemRemoveAsync(new ItemRemoveRequest { AccessToken = accessToken });
        ThrowIfError(response.Error, response.IsSuccessStatusCode, "remove item");
    }

    public async Task<PlaidAccountsResult> GetAccountsAsync(string accessToken, CancellationToken ct)
    {
        var response = await plaid.AccountsGetAsync(new AccountsGetRequest { AccessToken = accessToken });
        ThrowIfError(response.Error, response.IsSuccessStatusCode, "get accounts");

        var institutionId = response.Item?.InstitutionId ?? "";
        var accounts = response.Accounts.Select(a => new PlaidLinkedAccount(
            a.AccountId,
            a.Name,
            a.Mask ?? "",
            MapAccountType(a.Type),
            MapAccountSubtype(a.Subtype))).ToList();

        return new PlaidAccountsResult(accounts, institutionId);
    }

    public async Task<PlaidSyncResult> SyncTransactionsAsync(string accessToken, string cursor, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= MaxSyncPaginationRestarts; attempt++)
        {
            if (attempt > 0)
            {
                // Plaid data is still mutating — wait before retrying so the
                // underlying changes have time to settle. Without this pause
                // the restarts hammer the same in-flux data and fail identically.
                var delay = TimeSpan.FromSeconds(attempt * 2);
                logger.LogWarning(
                    "plaid.sync.mutation_during_pagination attempt={Attempt} maxAttempts={MaxAttempts} delaySeconds={DelaySeconds}",
                    attempt, MaxSyncPaginationRestarts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }

            try
            {
                return await SyncTransactionsAllPagesAsync(accessToken, cursor, ct);
            }
            catch (PlaidMutationDuringPaginationException) when (attempt == MaxSyncPaginationRestarts)
            {
                logger.LogError(
                    "plaid.sync.mutation_during_pagination_exhausted maxAttempts={MaxAttempts}",
                    MaxSyncPaginationRestarts + 1);
                throw;
            }
            catch (PlaidMutationDuringPaginationException)
            {
                // Restart the whole fetch from the original cursor on the next loop iteration.
            }
        }

        throw new InvalidOperationException("plaid: sync transactions retry loop exited without returning or throwing");
    }

    // Drains every page of a single logical sync starting at cursor,
    // following has_more until Plaid reports no more pages. Persisting an
    // intermediate (has_more=true) cursor is unsafe — per Plaid's docs, only
    // the cursor returned once has_more is false is guaranteed stable, and
    // stopping early both silently drops later-page changes and risks a
    // MUTATION_DURING_PAGINATION error on the next scheduled sync.
    private async Task<PlaidSyncResult> SyncTransactionsAllPagesAsync(string accessToken, string cursor, CancellationToken ct)
    {
        var added = new List<PlaidImportedTransaction>();
        var modified = new List<PlaidImportedTransaction>();
        var removedIds = new List<string>();

        while (true)
        {
            var request = new TransactionsSyncRequest
            {
                AccessToken = accessToken,
                Cursor = string.IsNullOrEmpty(cursor) ? null : cursor,
                Count = MaxSyncPageSize,
                // Obsolete per Plaid's own OpenAPI spec (newer API versions include it by
                // default) but still required for category resolution on this client's pinned
                // v20200914 version — mirrors internal/plaid/client.go setting it explicitly.
#pragma warning disable CS0612
                Options = new TransactionsSyncRequestOptions { IncludePersonalFinanceCategory = true },
#pragma warning restore CS0612
            };

            var response = await plaid.TransactionsSyncAsync(request);
            if (response.Error?.ErrorCode == MutationDuringPagination)
            {
                throw new PlaidMutationDuringPaginationException();
            }

            ThrowIfError(response.Error, response.IsSuccessStatusCode, "sync transactions");

            added.AddRange(response.Added.Select(ToImportedTransaction).OfType<PlaidImportedTransaction>());
            modified.AddRange(response.Modified.Select(ToImportedTransaction).OfType<PlaidImportedTransaction>());
            removedIds.AddRange(response.Removed.Select(r => r.TransactionId));

            cursor = response.NextCursor;
            if (!response.HasMore)
            {
                return new PlaidSyncResult(added, modified, removedIds, cursor);
            }
        }
    }

    private static PlaidImportedTransaction? ToImportedTransaction(Transaction t)
    {
        // Prefer authorized_date (when the purchase was made) over date (when
        // the bank settled it) — the posted date can lag 1-3 days, which
        // would mis-route a boundary transaction to the wrong period.
        var date = t.AuthorizedDate ?? t.Date;
        if (date is null)
        {
            return null;
        }

        // Prefer the Plaid-enriched merchant name when available — cleaner
        // and more human-readable than the raw bank string. Name is obsolete
        // per Plaid's spec but still populated on this client's API version.
#pragma warning disable CS0612
        var name = !string.IsNullOrEmpty(t.MerchantName) ? t.MerchantName : t.Name ?? "";
#pragma warning restore CS0612

        return new PlaidImportedTransaction(
            t.TransactionId ?? "",
            t.AccountId ?? "",
            name,
            t.Amount ?? 0m,
            date.Value,
            t.PersonalFinanceCategory?.Primary ?? "",
            t.PersonalFinanceCategory?.Detailed ?? "",
            t.PaymentMeta?.ReferenceNumber ?? "",
            t.PaymentMeta?.PpdId ?? "",
            t.PendingTransactionId ?? "");
    }

    // Going.Plaid's AccountType enum has no "brokerage" member — Plaid's API
    // itself has deprecated that as a top-level account type in favor of the
    // investment/brokerage subtype split, so this branch is unreachable in
    // practice but kept for parity with internal/plaid/account.go's PlaidPaymentTypeID.
    private static string MapAccountType(AccountType type) => type switch
    {
        AccountType.Depository => "depository",
        AccountType.Credit => "credit",
        AccountType.Investment => "investment",
        AccountType.Loan => "loan",
        AccountType.Other => "other",
        _ => "undefined",
    };

    // Only "savings" and "brokerage" need their exact wire value — every
    // other subtype falls through PlaidAccountMapping.PaymentTypeId's default
    // branch regardless of its precise string, so this isn't a full mapping.
    private static string MapAccountSubtype(AccountSubtype? subtype) => subtype switch
    {
        null => "",
        AccountSubtype.Savings => "savings",
        AccountSubtype.Brokerage => "brokerage",
        _ => subtype.Value.ToString(),
    };

    private static void ThrowIfError(PlaidError? error, bool isSuccess, string operation)
    {
        if (!isSuccess || error is not null)
        {
            throw new PlaidApiException(operation, error?.ErrorCode ?? "", error?.ErrorMessage ?? "");
        }
    }
}

/// <summary>Thrown when a Plaid API call returns a non-success response or a populated error body.</summary>
public sealed class PlaidApiException(string operation, string errorCode, string errorMessage)
    : Exception($"plaid: {operation}: {errorCode} {errorMessage}");

/// <summary>Signals TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION so SyncTransactionsAsync can restart the whole fetch from the original cursor.</summary>
public sealed class PlaidMutationDuringPaginationException : Exception;
