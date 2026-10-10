using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Plaid.ExchangePublicToken;

public sealed record ExchangePublicTokenCommand(Guid UserId, Guid BudgetProfileId, string PublicToken)
    : IRequest<PlaidConnectionDto>;

/// <summary>Mirrors Go's PlaidService.ExchangePublicToken. GetAccounts/GetInstitutionName failures are swallowed — the item is already stored, and payment methods can be created later via RefreshPlaidAccounts.</summary>
public sealed class ExchangePublicTokenCommandHandler(
    PlaidAccessGuard plaidAccess,
    BudgetAccessGuard budgetAccess,
    IPlaidItemRepository items,
    IPlaidClient plaid,
    ICryptoService crypto,
    IOptions<AuthOptions> options,
    PlaidPaymentMethodSync paymentMethodSync,
    PlaidBackgroundSync backgroundSync,
    ILogger<ExchangePublicTokenCommandHandler> logger) : IRequestHandler<ExchangePublicTokenCommand, PlaidConnectionDto>
{
    public async Task<PlaidConnectionDto> Handle(ExchangePublicTokenCommand request, CancellationToken ct)
    {
        await plaidAccess.RequireUsAsync(request.UserId, ct);
        await plaidAccess.RequireProOrLifetimeAsync(request.UserId, ct);
        await budgetAccess.EnsureMemberForbiddenAsync(request.BudgetProfileId, request.UserId, ct);

        var exchanged = await plaid.ExchangePublicTokenAsync(request.PublicToken, ct);
        var encryptedToken = crypto.Encrypt(exchanged.AccessToken, options.Value.EncryptionKey);

        var accounts = new List<PlaidLinkedAccount>();
        var institutionId = "";
        try
        {
            var result = await plaid.GetAccountsAsync(exchanged.AccessToken, ct);
            accounts = result.Accounts;
            institutionId = result.InstitutionId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "plaid.get_accounts_failed item_id={ItemId}", exchanged.ItemId);
        }

        string? institutionName = null;
        if (!string.IsNullOrEmpty(institutionId))
        {
            try
            {
                var name = await plaid.GetInstitutionNameAsync(institutionId, ct);
                if (!string.IsNullOrEmpty(name))
                {
                    institutionName = name;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "plaid.get_institution_name_failed institution_id={InstitutionId}", institutionId);
            }
        }

        var item = await items.CreateAsync(new PlaidItem
        {
            UserId = request.UserId,
            BudgetProfileId = request.BudgetProfileId,
            AccessToken = encryptedToken,
            ItemId = exchanged.ItemId,
            InstitutionId = string.IsNullOrEmpty(institutionId) ? null : institutionId,
            InstitutionName = institutionName,
        }, ct);

        var created = await paymentMethodSync.CreateMissingPaymentMethodsAsync(item, request.UserId, accounts, ct);
        logger.LogInformation(
            "plaid.item_connected plaid_item_id={PlaidItemId} item_id={ItemId} institution={InstitutionName} user_id={UserId} payment_methods_created={PaymentMethodsCreated}",
            item.Id, item.ItemId, institutionName ?? "", request.UserId, created);

        // Fired after the item is stored, so transactions appear right after
        // connecting rather than waiting for the next scheduled run.
        backgroundSync.FireAndForgetSyncItem(item.Id);

        return PlaidConnectionMapping.ToDto(item);
    }
}
