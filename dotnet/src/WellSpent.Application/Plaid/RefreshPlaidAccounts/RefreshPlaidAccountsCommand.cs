using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Plaid.RefreshPlaidAccounts;

public sealed record RefreshPlaidAccountsCommand(Guid UserId, Guid ConnectionId) : IRequest<PlaidConnectionDto>;

/// <summary>Mirrors Go's PlaidService.RefreshAccounts. Unlike ExchangePublicToken, decrypt/GetAccounts failures here are hard errors.</summary>
public sealed class RefreshPlaidAccountsCommandHandler(
    PlaidAccessGuard plaidAccess,
    IPlaidItemRepository items,
    IPlaidClient plaid,
    ICryptoService crypto,
    IOptions<AuthOptions> options,
    ITransactionRepository transactions,
    PlaidPaymentMethodSync paymentMethodSync,
    ILogger<RefreshPlaidAccountsCommandHandler> logger) : IRequestHandler<RefreshPlaidAccountsCommand, PlaidConnectionDto>
{
    public async Task<PlaidConnectionDto> Handle(RefreshPlaidAccountsCommand request, CancellationToken ct)
    {
        await plaidAccess.RequireUsAsync(request.UserId, ct);
        var item = await items.GetByIdAsync(request.ConnectionId, ct);
        if (item.UserId != request.UserId)
        {
            throw new ForbiddenException("access denied");
        }

        var accessToken = crypto.Decrypt(item.AccessToken, options.Value.EncryptionKey);
        var result = await plaid.GetAccountsAsync(accessToken, ct);

        var created = await paymentMethodSync.CreateMissingPaymentMethodsAsync(item, request.UserId, result.Accounts, ct);

        var stillPresent = result.Accounts.Select(a => a.PlaidAccountId).ToHashSet();
        var deactivated = 0;
        try
        {
            var existingMethods = await transactions.ListActivePaymentMethodsByPlaidItemIdAsync(item.Id, ct);
            foreach (var pm in existingMethods)
            {
                if (pm.PlaidAccountId is null || stillPresent.Contains(pm.PlaidAccountId))
                {
                    continue;
                }

                try
                {
                    await transactions.DeactivatePaymentMethodAsync(pm.Id, ct);
                    logger.LogInformation("plaid.refresh_deactivated_payment_method name={Name} plaid_item_id={PlaidItemId}", pm.Name, item.Id);
                    deactivated++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "plaid.refresh_deactivate_payment_method_failed payment_method_id={PaymentMethodId} plaid_item_id={PlaidItemId}", pm.Id, item.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "plaid.refresh_list_payment_methods_failed plaid_item_id={PlaidItemId}", item.Id);
        }

        logger.LogInformation(
            "plaid.item_refreshed plaid_item_id={PlaidItemId} item_id={ItemId} payment_methods_created={Created} payment_methods_deactivated={Deactivated}",
            item.Id, item.ItemId, created, deactivated);

        return PlaidConnectionMapping.ToDto(item);
    }
}
