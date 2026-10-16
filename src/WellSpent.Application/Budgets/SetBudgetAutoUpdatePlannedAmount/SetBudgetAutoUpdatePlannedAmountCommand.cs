using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.SetBudgetAutoUpdatePlannedAmount;

/// <summary>Admin-only — changes what every member's future periods are planned at.</summary>
public sealed record SetBudgetAutoUpdatePlannedAmountCommand(Guid UserId, Guid BudgetProfileId, bool Enabled)
    : IRequest<BudgetProfileDto>;

public sealed class SetBudgetAutoUpdatePlannedAmountCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<SetBudgetAutoUpdatePlannedAmountCommand, BudgetProfileDto>
{
    public async Task<BudgetProfileDto> Handle(SetBudgetAutoUpdatePlannedAmountCommand request, CancellationToken ct)
    {
        await access.EnsureAdminAsync(request.BudgetProfileId, request.UserId, ct);
        var profile = await profiles.SetAutoUpdatePlannedAmountAsync(request.BudgetProfileId, request.Enabled, ct);
        return mapper.Map<BudgetProfileDto>(profile);
    }
}
