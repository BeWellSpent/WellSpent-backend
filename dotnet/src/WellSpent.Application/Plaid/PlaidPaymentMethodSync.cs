using Microsoft.Extensions.Logging;
using WellSpent.Application.Abstractions;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Plaid;

/// <summary>
/// Shared between ExchangePublicTokenCommand (fresh connect) and
/// RefreshPlaidAccountsCommand (post update-mode) — mirrors Go's
/// createMissingPaymentMethods exactly. Creates a payment method for any
/// linked account not already represented (by PlaidAccountId, or by a
/// name-match fallback for accounts Plaid re-IDs on reconnect). Everything
/// here is non-fatal per account: the caller's PlaidItem is already
/// persisted regardless of outcome.
/// </summary>
public sealed class PlaidPaymentMethodSync(
    IBudgetProfileRepository budgets,
    ITransactionRepository transactions,
    ILogger<PlaidPaymentMethodSync> logger)
{
    public async Task<int> CreateMissingPaymentMethodsAsync(PlaidItem item, Guid userId, List<PlaidLinkedAccount> accounts, CancellationToken ct)
    {
        if (accounts.Count == 0)
        {
            return 0;
        }

        BudgetPerson person;
        try
        {
            person = await budgets.GetPersonByUserIdAsync(item.BudgetProfileId, userId, ct);
        }
        catch (NotFoundException)
        {
            return 0;
        }

        var created = 0;
        foreach (var account in accounts)
        {
            var name = PlaidAccountMapping.AccountName(account.Name, account.Mask);

            // Exact match by PlaidAccountId — same connection or a stable id.
            if (await transactions.GetPaymentMethodByPlaidAccountIdAsync(account.PlaidAccountId, ct) is not null)
            {
                continue;
            }

            // Name-based fallback — Plaid issues new account ids on reconnect. If a
            // method with the same name exists, relink it so future reconnects
            // dedup correctly, instead of creating a duplicate.
            var existingByName = await transactions.GetPaymentMethodByUserAndNameAsync(userId, name, ct);
            if (existingByName is not null)
            {
                try
                {
                    await transactions.UpdatePaymentMethodPlaidAccountIdAsync(existingByName.Id, account.PlaidAccountId, ct);
                }
                catch (Exception ex)
                {
                    // Future reconnects will duplicate this payment method instead of reusing it.
                    logger.LogError(ex, "plaid.link_payment_method_failed payment_method_id={PaymentMethodId} plaid_account_id={PlaidAccountId} plaid_item_id={PlaidItemId}",
                        existingByName.Id, account.PlaidAccountId, item.Id);
                }

                continue;
            }

            try
            {
                await transactions.CreatePaymentMethodAsync(new PaymentMethod
                {
                    Name = name,
                    PaymentTypeId = PlaidAccountMapping.PaymentTypeId(account.Type, account.Subtype),
                    UserId = userId,
                    BudgetPersonId = person.Id,
                    PlaidAccountId = account.PlaidAccountId,
                    PlaidItemId = item.Id,
                }, ct);
                logger.LogInformation("plaid.payment_method_created name={Name} plaid_account_id={PlaidAccountId} plaid_item_id={PlaidItemId}",
                    name, account.PlaidAccountId, item.Id);
                created++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "plaid.create_payment_method_failed plaid_account_id={PlaidAccountId} plaid_item_id={PlaidItemId}",
                    account.PlaidAccountId, item.Id);
            }
        }

        return created;
    }
}
