using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Application.FixedExpenses;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Plaid;

/// <summary>Mirrors plaid_sync.go: three entry points funnel into one per-item core. Safe to call from a background task.</summary>
public sealed class PlaidSyncEngine(
    IPlaidItemRepository items,
    IUserRepository users,
    IBudgetProfileRepository budgets,
    ITransactionRepository transactions,
    IFixedExpenseRepository fixedExpenses,
    ITransactionReviewRepository reviews,
    IPlaidClient plaid,
    ICryptoService crypto,
    IOptions<AuthOptions> options,
    ILogger<PlaidSyncEngine> logger)
{
    private const int VariableTypeId = 2;
    private const int OneOffFrequencyId = 1;

    /// <summary>Syncs every connection currently due, grouped by budget profile — entitlement, periods, and notifications are all per-profile.</summary>
    public async Task<List<ProfileSyncResult>> SyncAllAsync(CancellationToken ct)
    {
        var dueItems = await items.ListActiveForSyncAsync(ct);
        var profiles = new List<ProfileSyncResult>();
        var indexByProfile = new Dictionary<Guid, int>();

        foreach (var item in dueItems)
        {
            var result = await SyncItemCoreAsync(item, ct);
            if (!indexByProfile.TryGetValue(item.BudgetProfileId, out var idx))
            {
                profiles.Add(new ProfileSyncResult { ProfileId = item.BudgetProfileId });
                idx = profiles.Count - 1;
                indexByProfile[item.BudgetProfileId] = idx;
            }

            profiles[idx].Items.Add(result);
        }

        foreach (var profile in profiles)
        {
            await NotifyProfileAsync(profile, ct);
        }

        return profiles;
    }

    /// <summary>Forces an immediate sync of one budget profile's connections, bypassing SyncAllAsync's daily cooldown — used by cycle-budgets right before a profile's period closes (B7, issue #68).</summary>
    public async Task<ProfileSyncResult> SyncProfileAsync(Guid profileId, CancellationToken ct)
    {
        var dueItems = await items.ListActiveForProfileSyncAsync(profileId, ct);
        var result = new ProfileSyncResult { ProfileId = profileId };
        foreach (var item in dueItems)
        {
            result.Items.Add(await SyncItemCoreAsync(item, ct));
        }

        await NotifyProfileAsync(result, ct);
        return result;
    }

    /// <summary>Syncs a single connection and notifies for it directly — used for the immediate sync fired after a connection is created or resynced, where there's no run to aggregate into.</summary>
    public async Task<ItemSyncResult> SyncItemAsync(PlaidItem item, CancellationToken ct)
    {
        var result = await SyncItemCoreAsync(item, ct);
        await NotifyProfileAsync(new ProfileSyncResult { ProfileId = item.BudgetProfileId, Items = [result] }, ct);
        return result;
    }

    private Task NotifyProfileAsync(ProfileSyncResult profile, CancellationToken ct)
    {
        // HOOK: notify budget members (Notification domain's event-dispatch wiring isn't done yet).
        return Task.CompletedTask;
    }

    private async Task<ItemSyncResult> SyncItemCoreAsync(PlaidItem item, CancellationToken ct)
    {
        var result = new ItemSyncResult { ItemId = item.Id, InstitutionName = item.InstitutionName ?? "" };

        // Entitled per connection owner, not per budget. A failed owner lookup does not skip the sync.
        try
        {
            var owner = await users.GetByIdAsync(item.UserId, ct);
            if (owner.Plan == "free")
            {
                logger.LogInformation("plaid.sync_skipped_unentitled plaid_item_id={PlaidItemId} institution={InstitutionName} owner_id={OwnerId}",
                    item.Id, result.InstitutionName, item.UserId);
                result.SkippedUnentitled = true;
                return result;
            }
        }
        catch (Exception)
        {
            // Fall through — owner lookup failing is not itself a reason to skip.
        }

        Dictionary<string, int> categoryIds;
        try
        {
            categoryIds = await transactions.ListSystemCategoriesAsync(ct);
        }
        catch (Exception ex)
        {
            result.Error = ex;
            return result;
        }

        string accessToken;
        try
        {
            accessToken = crypto.Decrypt(item.AccessToken, options.Value.EncryptionKey);
        }
        catch (Exception ex)
        {
            result.Error = ex;
            return result;
        }

        PlaidSyncResult sync;
        try
        {
            sync = await plaid.SyncTransactionsAsync(accessToken, item.Cursor ?? "", ct);
        }
        catch (Exception ex)
        {
            try
            {
                await items.UpdateStatusAsync(item.Id, "error", ct);
            }
            catch (Exception statusEx)
            {
                // Connection stays reading "active" while actually broken.
                logger.LogError(statusEx, "plaid.sync_mark_errored_failed plaid_item_id={PlaidItemId}", item.Id);
            }

            result.Error = ex;
            return result;
        }

        logger.LogInformation("plaid.sync_fetched plaid_item_id={PlaidItemId} added={Added} modified={Modified} removed={Removed}",
            item.Id, sync.Added.Count, sync.Modified.Count, sync.RemovedIds.Count);

        // Resolved once per Plaid account: payment method + name to report it under.
        var pmCache = new Dictionary<string, (Guid? PaymentMethodId, string Name)>();
        var byAccount = new Dictionary<string, int>();

        var fixedExpenseList = await fixedExpenses.ListAsync(item.BudgetProfileId, ct);
        var aliasesByFe = new Dictionary<Guid, List<string>>();
        foreach (var fe in fixedExpenseList)
        {
            aliasesByFe[fe.Id] = await reviews.ListAliasesAsync(fe.Id, ct);
        }

        var importedAdded = 0;
        var autoConfirmed = 0;
        var queued = 0;
        var skippedNoPeriod = 0;
        var skippedDuplicate = 0;
        var repointedCount = 0;

        // Read once per item, not per transaction: it cannot change mid-run, and a sync can import hundreds of rows.
        var autoUpdatePlanned = await FixedExpensePaymentSync.AutoUpdatePlannedAmountForAsync(budgets, item.BudgetProfileId, ct);

        foreach (var tx in sync.Added)
        {
            var period = await budgets.GetPeriodByDateAsync(item.BudgetProfileId, tx.Date, ct);
            if (period is null)
            {
                skippedNoPeriod++;
                logger.LogInformation("plaid.sync_skipped_no_period plaid_item_id={PlaidItemId} name={Name} date={Date} amount={Amount} plaid_id={PlaidId}",
                    item.Id, tx.Name, tx.Date, tx.Amount, tx.PlaidId);
                continue;
            }

            // A settled pending transaction arrives under a new PlaidId — repoint in place (issue #67).
            if (!string.IsNullOrEmpty(tx.PendingTransactionId))
            {
                var existing = await transactions.GetTransactionByPlaidIdAsync(tx.PendingTransactionId, ct);
                if (existing is not null)
                {
                    if (await SettlePendingTransactionAsync(item.Id, tx, existing, autoUpdatePlanned, ct))
                    {
                        repointedCount++;
                    }

                    continue;
                }
            }

            if (await transactions.ExistsTransactionByPlaidIdAsync(tx.PlaidId, ct))
            {
                skippedDuplicate++;
                logger.LogInformation("plaid.sync_skipped_duplicate plaid_item_id={PlaidItemId} name={Name} plaid_id={PlaidId}", item.Id, tx.Name, tx.PlaidId);
                continue;
            }

            var (categoryKey, categoryId) = PlaidSyncCategoryResolution.ResolveId(tx.Name, tx.PfcPrimary, tx.PfcDetailed, categoryIds);

            Guid? paymentMethodId = null;
            var accountName = result.InstitutionName;
            if (!string.IsNullOrEmpty(tx.AccountId))
            {
                if (!pmCache.TryGetValue(tx.AccountId, out var cached))
                {
                    cached = (null, result.InstitutionName);
                    var pm = await transactions.GetPaymentMethodByPlaidAccountIdAsync(tx.AccountId, ct);
                    if (pm is not null)
                    {
                        cached = (pm.Id, pm.Name);
                    }

                    pmCache[tx.AccountId] = cached;
                }

                paymentMethodId = cached.PaymentMethodId;
                accountName = cached.Name;
            }

            if (string.IsNullOrEmpty(accountName))
            {
                accountName = "Unknown account";
            }

            Transaction inserted;
            try
            {
                inserted = await transactions.CreateTransactionAsync(new Transaction
                {
                    Name = tx.Name,
                    Amount = tx.Amount,
                    PlannedAmount = tx.Amount,
                    Date = tx.Date,
                    BudgetPeriodId = period.Id,
                    CategoryId = categoryId,
                    PaymentMethodId = paymentMethodId,
                    TransactionFrequencyId = OneOffFrequencyId,
                    TransactionTypeId = VariableTypeId,
                    PlaidTransactionId = tx.PlaidId,
                    // Plaid's own classification, kept so a later re-classification stays possible.
                    PlaidPfcPrimary = EmptyToNull(tx.PfcPrimary),
                    PlaidPfcDetailed = EmptyToNull(tx.PfcDetailed),
                    PlaidReferenceNumber = EmptyToNull(tx.ReferenceNumber),
                    PlaidPpdId = EmptyToNull(tx.PpdId),
                }, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "plaid.sync_insert_failed plaid_item_id={PlaidItemId} plaid_id={PlaidId}", item.Id, tx.PlaidId);
                continue;
            }

            logger.LogInformation("plaid.sync_imported plaid_item_id={PlaidItemId} name={Name} date={Date} amount={Amount} category={Category}",
                item.Id, tx.Name, tx.Date, tx.Amount, CategoryLogValue(categoryKey, categoryId));
            importedAdded++;

            // Unconsumed = "newly available", counted in the per-account summary.
            var consumed = false;

            var (bestScore, bestFe, bestAliasHit, bestAmountOk) =
                PlaidSyncMatching.ScoreBestMatch(tx.Name, tx.Amount, categoryId, paymentMethodId, fixedExpenseList, aliasesByFe);
            if (bestFe is null)
            {
                byAccount[accountName] = byAccount.GetValueOrDefault(accountName) + 1;
                continue;
            }

            // Archived periods block mark-paid/exclude everywhere else; the sync must too.
            if (period.IsArchived)
            {
                byAccount[accountName] = byAccount.GetValueOrDefault(accountName) + 1;
                logger.LogInformation("plaid.sync_imported_into_archived_period plaid_item_id={PlaidItemId} name={Name} period_id={PeriodId}",
                    item.Id, tx.Name, period.Id);
                continue;
            }

            // Scoped to this transaction's own period, not every live one (issue #41).
            var unpaid = await fixedExpenses.GetUnpaidTransactionInPeriodAsync(bestFe.Id, period.Id, ct);
            var hasUnpaidTarget = unpaid is not null;

            if (bestAliasHit && bestAmountOk && hasUnpaidTarget)
            {
                consumed = await TryAutoConfirmAsync(item.Id, tx, period, inserted, unpaid!, bestFe, bestScore, categoryId, paymentMethodId, autoUpdatePlanned, byAccount, accountName, ct);
                if (consumed) autoConfirmed++;
            }
            else if (bestScore >= 80 && hasUnpaidTarget)
            {
                try
                {
                    await reviews.UpsertAsync(period.Id, inserted.Id, unpaid!.Id, (decimal)bestScore, ct);
                    queued++;
                    consumed = true;
                    logger.LogInformation("plaid.sync_queued_review plaid_item_id={PlaidItemId} name={Name} score={Score} fixed_expense={FixedExpenseName}",
                        item.Id, tx.Name, bestScore, bestFe.Name);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "plaid.sync_queue_review_failed plaid_item_id={PlaidItemId} name={Name}", item.Id, tx.Name);
                }
            }

            if (!consumed)
            {
                byAccount[accountName] = byAccount.GetValueOrDefault(accountName) + 1;
            }
        }

        foreach (var tx in sync.Modified)
        {
            try
            {
                await transactions.UpdateTransactionFromPlaidAsync(tx.PlaidId, tx.Name, tx.Amount, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "plaid.sync_update_failed plaid_item_id={PlaidItemId} plaid_id={PlaidId}", item.Id, tx.PlaidId);
                continue;
            }

            logger.LogInformation("plaid.sync_updated plaid_item_id={PlaidItemId} name={Name} amount={Amount}", item.Id, tx.Name, tx.Amount);
        }

        // A pending id already repointed above no longer matches here, so this delete no-ops for it.
        foreach (var plaidId in sync.RemovedIds)
        {
            try
            {
                await transactions.DeleteTransactionByPlaidIdAsync(plaidId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "plaid.sync_delete_failed plaid_item_id={PlaidItemId} plaid_id={PlaidId}", item.Id, plaidId);
                continue;
            }

            logger.LogInformation("plaid.sync_removed plaid_item_id={PlaidItemId} plaid_id={PlaidId}", item.Id, plaidId);
        }

        // A cursor-persist failure re-fetches the same batch next run (harmless) but must still surface.
        Exception? cursorError = null;
        try
        {
            await items.UpdateSyncAsync(item.Id, sync.NextCursor, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.sync_update_cursor_failed plaid_item_id={PlaidItemId}", item.Id);
            cursorError = ex;
        }

        result.Imported = importedAdded;
        result.AutoConfirmed = autoConfirmed;
        result.Queued = queued;
        result.SkippedNoPeriod = skippedNoPeriod;
        result.SkippedDuplicate = skippedDuplicate;
        result.Modified = sync.Modified.Count;
        result.Removed = sync.RemovedIds.Count;
        result.Repointed = repointedCount;
        result.ByAccount = SortedAccountImports(byAccount);
        result.Error = cursorError;

        logger.LogInformation(
            "plaid.sync_done plaid_item_id={PlaidItemId} imported={Imported} auto_confirmed={AutoConfirmed} queued={Queued} modified={Modified} removed={Removed} repointed={Repointed} skipped_no_period={SkippedNoPeriod} skipped_duplicate={SkippedDuplicate}",
            item.Id, importedAdded, autoConfirmed, queued, sync.Modified.Count, sync.RemovedIds.Count, repointedCount, skippedNoPeriod, skippedDuplicate);

        return result;
    }

    // Each of the four steps has its own try/catch — collapsing them would hide which one failed.
    private async Task<bool> TryAutoConfirmAsync(
        Guid itemId, PlaidImportedTransaction tx, BudgetPeriod period, Transaction inserted, Transaction unpaid,
        FixedExpense bestFe, double bestScore, int? categoryId, Guid? paymentMethodId, bool autoUpdatePlanned,
        Dictionary<string, int> byAccount, string accountName, CancellationToken ct)
    {
        try
        {
            await FixedExpensePaymentSync.MarkPaidAsync(
                transactions, fixedExpenses, unpaid.Id, period.Id, tx.Amount, tx.Date,
                autoUpdatePlanned, new ObservedPayment(categoryId, paymentMethodId), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.sync_auto_confirm_mark_paid_failed plaid_item_id={PlaidItemId} name={Name}", itemId, tx.Name);
            byAccount[accountName] = byAccount.GetValueOrDefault(accountName) + 1;
            return false;
        }

        TransactionReview review;
        try
        {
            review = await reviews.UpsertAsync(period.Id, inserted.Id, unpaid.Id, (decimal)bestScore, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.sync_auto_confirm_create_review_failed plaid_item_id={PlaidItemId} name={Name}", itemId, tx.Name);
            byAccount[accountName] = byAccount.GetValueOrDefault(accountName) + 1;
            return false;
        }

        try
        {
            await reviews.UpdateStatusAsync(review.Id, "confirmed", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.sync_auto_confirm_update_review_failed plaid_item_id={PlaidItemId} name={Name} review_id={ReviewId}", itemId, tx.Name, review.Id);
            byAccount[accountName] = byAccount.GetValueOrDefault(accountName) + 1;
            return false;
        }

        try
        {
            await transactions.SetTransactionExcludedAsync(inserted.Id, period.Id, true, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.sync_auto_confirm_exclude_failed plaid_item_id={PlaidItemId} name={Name}", itemId, tx.Name);
            byAccount[accountName] = byAccount.GetValueOrDefault(accountName) + 1;
            return false;
        }

        logger.LogInformation("plaid.sync_auto_confirmed plaid_item_id={PlaidItemId} name={Name} fixed_expense={FixedExpenseName}", itemId, tx.Name, bestFe.Name);
        return true;
    }

    // Mirrors Go's settlePendingTransaction — UPDATE-in-place, never delete+reinsert (issue #67).
    private async Task<bool> SettlePendingTransactionAsync(Guid itemId, PlaidImportedTransaction tx, Transaction existing, bool autoUpdatePlanned, CancellationToken ct)
    {
        Transaction repointed;
        try
        {
            repointed = await transactions.RepointTransactionPlaidIdAsync(tx.PendingTransactionId, tx.PlaidId, tx.Name, tx.Amount, tx.Date, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.settle_repoint_failed plaid_item_id={PlaidItemId} old_plaid_id={OldPlaidId} new_plaid_id={NewPlaidId}",
                itemId, tx.PendingTransactionId, tx.PlaidId);
            return false;
        }

        logger.LogInformation("plaid.settle_repointed plaid_item_id={PlaidItemId} name={Name} date={Date} amount={Amount} old_plaid_id={OldPlaidId} new_plaid_id={NewPlaidId}",
            itemId, tx.Name, tx.Date, tx.Amount, tx.PendingTransactionId, tx.PlaidId);

        var review = await reviews.GetByTransactionIdAsync(existing.Id, ct);
        if (review is null || review.Status != "confirmed")
        {
            return true;
        }

        if (repointed.Amount == existing.Amount)
        {
            return true;
        }

        Transaction matchedTx;
        try
        {
            matchedTx = await transactions.GetTransactionAsync(review.MatchedTransactionId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.settle_read_matched_failed plaid_item_id={PlaidItemId} matched_transaction_id={MatchedTransactionId}",
                itemId, review.MatchedTransactionId);
            return true;
        }

        if (matchedTx.BudgetPeriodId is not { } matchedPeriodId)
        {
            return true;
        }

        try
        {
            // Re-syncing a settled amount, not a fresh payment — no ObservedPayment override.
            await FixedExpensePaymentSync.MarkPaidAsync(
                transactions, fixedExpenses, matchedTx.Id, matchedPeriodId, repointed.Amount,
                matchedTx.PaidDate ?? tx.Date, autoUpdatePlanned, default, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.settle_update_paid_amount_failed plaid_item_id={PlaidItemId} matched_transaction_id={MatchedTransactionId}",
                itemId, review.MatchedTransactionId);
            return true;
        }

        logger.LogInformation("plaid.settle_paid_amount_updated plaid_item_id={PlaidItemId} matched_transaction_id={MatchedTransactionId} amount={Amount}",
            itemId, review.MatchedTransactionId, tx.Amount);
        return true;
    }

    private static string? EmptyToNull(string v) => string.IsNullOrEmpty(v) ? null : v;

    // Distinguishes an "unmapped" import from a correctly categorized one in the logs.
    private static string CategoryLogValue(string categoryKey, int? categoryId) => categoryId is not null
        ? categoryKey
        : categoryKey.Length > 0 ? $"{categoryKey} (unmapped — no matching system category)" : "none";

    private static List<AccountImport> SortedAccountImports(Dictionary<string, int> totals) =>
        totals.Count == 0
            ? []
            : totals
                .Select(kv => new AccountImport(kv.Key, kv.Value))
                .OrderByDescending(a => a.Count)
                .ThenBy(a => a.Account, StringComparer.Ordinal)
                .ToList();
}
