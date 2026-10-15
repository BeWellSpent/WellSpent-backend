using MediatR;
using Microsoft.Extensions.Options;
using WellSpent.Application.Abstractions;
using WellSpent.Application.Common;
using WellSpent.Application.Configuration;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Plaid.CreateLinkToken;

public sealed record CreateLinkTokenCommand(Guid UserId, Guid BudgetProfileId, Guid? ConnectionId, string RedirectUri)
    : IRequest<PlaidLinkToken>;

/// <summary>Mirrors Go's PlaidService.CreateLinkToken exactly, including requireUS/requireProOrLifetime running before the membership check.</summary>
public sealed class CreateLinkTokenCommandHandler(
    PlaidAccessGuard plaidAccess,
    BudgetAccessGuard budgetAccess,
    IPlaidItemRepository items,
    IPlaidClient plaid,
    ICryptoService crypto,
    IOptions<AuthOptions> options) : IRequestHandler<CreateLinkTokenCommand, PlaidLinkToken>
{
    public async Task<PlaidLinkToken> Handle(CreateLinkTokenCommand request, CancellationToken ct)
    {
        await plaidAccess.RequireUsAsync(request.UserId, ct);
        await plaidAccess.RequireProOrLifetimeAsync(request.UserId, ct);
        await budgetAccess.EnsureMemberForbiddenAsync(request.BudgetProfileId, request.UserId, ct);

        var updateAccessToken = "";
        if (request.ConnectionId is { } connectionId)
        {
            var item = await items.GetByIdAsync(connectionId, ct);
            if (item.UserId != request.UserId)
            {
                throw new ForbiddenException("access denied");
            }

            // Hard error, not swallowed — update mode needs a real access token.
            updateAccessToken = crypto.Decrypt(item.AccessToken, options.Value.EncryptionKey);
        }

        return await plaid.CreateLinkTokenAsync(request.UserId.ToString(), updateAccessToken, request.RedirectUri, ct);
    }
}
