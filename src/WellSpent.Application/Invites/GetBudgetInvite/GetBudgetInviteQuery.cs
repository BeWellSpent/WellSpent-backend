using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Invites.GetBudgetInvite;

/// <summary>Public — no auth required. Drives the invite preview page shown before the user authenticates.</summary>
public sealed record GetBudgetInviteQuery(Guid Token) : IRequest<BudgetInviteDto>;

public sealed class GetBudgetInviteQueryHandler(
    IInviteRepository invites, IBudgetProfileRepository profiles, IUserRepository users,
    ILogger<GetBudgetInviteQueryHandler> logger) : IRequestHandler<GetBudgetInviteQuery, BudgetInviteDto>
{
    public async Task<BudgetInviteDto> Handle(GetBudgetInviteQuery request, CancellationToken ct)
    {
        var invite = await InviteLookup.GetValidAsync(invites, logger, request.Token, ct);

        var profile = await profiles.GetByIdAsync(invite.BudgetProfileId, ct);
        var inviter = await users.GetByIdAsync(invite.InvitedBy, ct);
        return InviteDisplay.ToDto(invite, profile.Name, InviteDisplay.InviterName(inviter));
    }
}
