using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Invites.ListBudgetInvites;

public sealed record ListBudgetInvitesQuery(Guid CallerId, Guid BudgetProfileId) : IRequest<List<BudgetInviteDto>>;

public sealed class ListBudgetInvitesQueryHandler(IInviteRepository invites, BudgetAccessGuard access)
    : IRequestHandler<ListBudgetInvitesQuery, List<BudgetInviteDto>>
{
    public async Task<List<BudgetInviteDto>> Handle(ListBudgetInvitesQuery request, CancellationToken ct)
    {
        await access.EnsureAdminAsync(request.BudgetProfileId, request.CallerId, ct);
        var rows = await invites.ListByProfileAsync(request.BudgetProfileId, ct);

        // budgetName/inviterName are deliberately empty — mirrors Go's
        // handler exactly (toProtoInvite(inv, "", "")). The caller is
        // managing invites for a budget they're already on.
        return rows.Select(r => InviteDisplay.ToDto(r, "", "")).ToList();
    }
}
