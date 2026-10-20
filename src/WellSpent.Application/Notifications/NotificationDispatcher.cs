using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Application.Plaid;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Notifications;

/// <summary>
/// Mirrors Go's NotificationService "trigger hooks" + delivery section
/// exactly: one Handle* method per event other domains fire, fanning out
/// through the same deliver()/sendInApp()/sendEmail()/sendPush() pipeline.
/// Every failure here is logged and swallowed, same posture as Go: the
/// triggering action (a transaction, a period, a sync) already succeeded
/// and must not be undone by a notification failure.
/// </summary>
public sealed class NotificationDispatcher(
    INotificationRepository notifications,
    IBudgetProfileRepository profiles,
    IUserRepository users,
    IPushSender pushSender,
    ITransactionRepository transactions,
    IExpenseAllocationRepository allocations,
    IEmailSender emailSender,
    IOptions<AuthOptions> options,
    ILogger<NotificationDispatcher> logger)
{
    /// <summary>Fires when a variable transaction is created. Notifies OTHER subscribed budget members and checks spending_threshold crossings.</summary>
    public async Task HandleNewTransactionAsync(Transaction tx, Guid periodId, Guid callerUserId, CancellationToken ct)
    {
        BudgetPeriod period;
        try
        {
            period = await profiles.GetPeriodByIdAsync(periodId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "notification.handle_new_transaction_get_period_failed period_id={PeriodId}", periodId);
            return;
        }
        var profileId = period.BudgetProfileId;
        var txName = tx.Name ?? "(unnamed)";

        var subs = await GetSubscribersSafeAsync(profileId, "new_transaction", "handle_new_transaction", ct);
        foreach (var sub in subs)
        {
            if (sub.UserId == callerUserId) continue; // don't notify the person who created the transaction
            await DeliverAsync(sub, profileId, "new_transaction", "New transaction added", $"A new transaction was recorded: {txName}", ct);
        }

        await CheckSpendingThresholdAsync(tx, periodId, profileId, ct);
    }

    /// <summary>Fires when a Plaid-imported or manually-matched transaction is queued for review. One transaction at a time.</summary>
    public async Task HandleReviewPendingAsync(Guid profileId, string txName, CancellationToken ct)
    {
        var subs = await GetSubscribersSafeAsync(profileId, "review_pending", "handle_review_pending", ct);
        foreach (var sub in subs)
        {
            await DeliverAsync(sub, profileId, "review_pending", "Transaction pending review", $"A new import needs your review: {txName}", ct);
        }
    }

    /// <summary>Plaid-sync counterpart to HandleReviewPendingAsync: one aggregated notification per sync run rather than one per transaction.</summary>
    public async Task HandleReviewPendingBatchAsync(Guid profileId, int count, CancellationToken ct)
    {
        if (count <= 0) return;
        var subs = await GetSubscribersSafeAsync(profileId, "review_pending", "handle_review_pending_batch", ct);
        var body = count > 1 ? $"{count} new imports need your review." : "A new import needs your review.";
        foreach (var sub in subs)
        {
            await DeliverAsync(sub, profileId, "review_pending", "Transactions pending review", body, ct);
        }
    }

    /// <summary>Fires once per Plaid sync run when one or more new Variable transactions were imported that didn't need review. Every subscriber is notified — there's no caller to exclude.</summary>
    public async Task HandlePlaidTransactionsImportedAsync(Guid profileId, List<AccountImport> imports, CancellationToken ct)
    {
        var total = imports.Sum(i => i.Count);
        if (total <= 0) return;
        var subs = await GetSubscribersSafeAsync(profileId, "new_transaction", "handle_plaid_transactions_imported", ct);
        var body = PlaidImportBody(imports, total);
        foreach (var sub in subs)
        {
            await DeliverAsync(sub, profileId, "new_transaction", "New transactions imported", body, ct);
        }
    }

    /// <summary>Fires when createNextPeriod creates a new budget period.</summary>
    public async Task HandlePeriodCreatedAsync(BudgetPeriod period, CancellationToken ct)
    {
        var profileId = period.BudgetProfileId;
        var subs = await GetSubscribersSafeAsync(profileId, "period_created", "handle_period_created", ct);
        var start = period.StartDate.ToString("MMM d");
        var end = period.EndDate.ToString("MMM d, yyyy");
        var body = $"Your budget period {start} – {end} is now active.";
        foreach (var sub in subs)
        {
            await DeliverAsync(sub, profileId, "period_created", "New budget period started", body, ct);
        }
    }

    // ── Spending threshold ──────────────────────────────────────────────────

    private async Task CheckSpendingThresholdAsync(Transaction newTx, Guid periodId, Guid profileId, CancellationToken ct)
    {
        var subs = await GetSubscribersSafeAsync(profileId, "spending_threshold", "check_spending_threshold", ct);
        if (subs.Count == 0) return;

        List<ExpenseAllocation> allocs;
        List<Transaction> txs;
        try
        {
            allocs = await allocations.ListAsync(profileId, ct) ?? [];
            txs = await transactions.ListTransactionsAsync(periodId, null, VariableTypeId, null, ct) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "notification.check_spending_threshold_list_failed profile_id={ProfileId}", profileId);
            return;
        }

        var catSums = new Dictionary<int, decimal>();
        var totalSum = 0m;
        foreach (var t in txs)
        {
            if (t.IsExcluded) continue;
            totalSum += t.Amount;
            if (t.CategoryId is { } catId)
            {
                catSums[catId] = catSums.GetValueOrDefault(catId) + t.Amount;
            }
        }

        var catPlan = new Dictionary<int, decimal>();
        var totalPlan = 0m;
        foreach (var a in allocs)
        {
            totalPlan += a.PlannedAmount;
            catPlan[a.CategoryId] = catPlan.GetValueOrDefault(a.CategoryId) + a.PlannedAmount;
        }

        var prevTotal = totalSum - newTx.Amount;
        var newTxCatId = newTx.CategoryId ?? 0;
        var prevCatSum = catSums.GetValueOrDefault(newTxCatId) - newTx.Amount;

        foreach (var sub in subs)
        {
            var threshold = sub.ThresholdPct ?? 0;
            if (threshold <= 0) continue;
            var scope = sub.ThresholdScope ?? "budget";

            decimal planAmt, prevSum, newSum;
            if (scope == "category")
            {
                var catId = sub.CategoryId ?? 0;
                if (catId == 0) continue;
                if (newTx.CategoryId != catId) continue; // this transaction isn't in the subscribed category — no crossing possible
                planAmt = catPlan.GetValueOrDefault(catId);
                prevSum = prevCatSum;
                newSum = catSums.GetValueOrDefault(catId);
            }
            else
            {
                planAmt = totalPlan;
                prevSum = prevTotal;
                newSum = totalSum;
            }

            if (planAmt <= 0) continue;
            var pct = threshold / 100m;
            var crossed = prevSum < pct * planAmt && newSum >= pct * planAmt;
            if (!crossed) continue;

            var txName = newTx.Name ?? "(unnamed)";
            var scopeLabel = scope == "category" ? "category" : "budget";
            var title = $"Spending alert: {threshold:0}% of {scopeLabel} plan reached";
            var body = $"Adding \"{txName}\" brought your {scopeLabel} spending to {(newSum / planAmt) * 100:0}% of the plan.";
            await DeliverAsync(sub, profileId, "spending_threshold", title, body, ct);
        }
    }

    private const int VariableTypeId = 2;

    private static string PlaidImportBody(List<AccountImport> imports, int total)
    {
        if (total == 1)
        {
            return imports.Count == 1
                ? $"A new transaction was imported from {imports[0].Account}."
                : "A new transaction was imported from your bank.";
        }
        if (imports.Count == 1)
        {
            return $"{total} new transactions were imported from {imports[0].Account}.";
        }
        var lines = imports.Select(i => $"{i.Account} — {i.Count}");
        return $"{total} new transactions were imported from your banks:\n{string.Join('\n', lines)}";
    }

    // ── Delivery ─────────────────────────────────────────────────────────────

    private async Task<List<AlertSubscription>> GetSubscribersSafeAsync(Guid profileId, string alertType, string op, CancellationToken ct)
    {
        try
        {
            return await notifications.GetBudgetSubscribersAsync(profileId, alertType, ct) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "notification.{Op}_get_subscribers_failed profile_id={ProfileId}", op, profileId);
            return [];
        }
    }

    /// <summary>Creates an in-app notification and, if subscribed by email, sends one too. Fans out to every active budget member when the subscription's NotifyAllMembers is set.</summary>
    private async Task DeliverAsync(AlertSubscription sub, Guid profileId, string alertType, string title, string body, CancellationToken ct)
    {
        var recipients = new List<Guid> { sub.UserId };
        if (sub.NotifyAllMembers)
        {
            try
            {
                var people = await profiles.ListPeopleAsync(profileId, ct) ?? [];
                recipients = people.Where(p => p.IsActive && p.UserId is not null).Select(p => p.UserId!.Value).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "notification.deliver_list_people_failed profile_id={ProfileId}", profileId);
            }
        }

        foreach (var userId in recipients)
        {
            await SendInAppAsync(userId, profileId, alertType, title, body, ct);
            if (sub.Channel == "email" || sub.Channel == "both")
            {
                await SendEmailAsync(userId, title, body, ct);
            }
        }
    }

    private async Task SendInAppAsync(Guid userId, Guid profileId, string alertType, string title, string body, CancellationToken ct)
    {
        try
        {
            await notifications.CreateAsync(new Notification
            {
                UserId = userId,
                BudgetProfileId = profileId,
                AlertType = alertType,
                Title = title,
                Body = body,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "notification.send_in_app_failed user_id={UserId}", userId);
            return;
        }

        // Push rides along with every in-app notification, for every device the user has registered.
        try
        {
            var tokens = await notifications.ListDeviceTokensForUserAsync(userId, ct) ?? [];
            foreach (var token in tokens)
            {
                await pushSender.SendAsync(token.Token, title, body, alertType, profileId, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "notification.send_push_list_tokens_failed user_id={UserId}", userId);
        }
    }

    private async Task SendEmailAsync(Guid userId, string subject, string body, CancellationToken ct)
    {
        User user;
        try
        {
            user = await users.GetByIdAsync(userId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "notification.send_email_get_user_failed user_id={UserId}", userId);
            return;
        }

        var wrapped = $"<p>{body.Replace("\n", "<br>")}</p>" +
            $"<p style=\"color:#888;font-size:12px\">You are receiving this email because you subscribed to budget alerts in WellSpent. " +
            $"<a href=\"{options.Value.FrontendUrl.TrimEnd('/')}\">Manage alerts</a></p>";

        try
        {
            await emailSender.SendAsync(user.Email, subject, wrapped, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "notification.send_email_failed to={Email}", user.Email);
        }
    }
}
