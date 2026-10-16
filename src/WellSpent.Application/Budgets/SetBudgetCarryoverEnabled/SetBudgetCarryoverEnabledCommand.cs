using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.SetBudgetCarryoverEnabled;

/// <summary>Admin-only — changes what every member's next period will contain.</summary>
public sealed record SetBudgetCarryoverEnabledCommand(Guid UserId, Guid BudgetProfileId, bool Enabled)
    : IRequest<BudgetProfileDto>;

public sealed class SetBudgetCarryoverEnabledCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<SetBudgetCarryoverEnabledCommand, BudgetProfileDto>
{
    public async Task<BudgetProfileDto> Handle(SetBudgetCarryoverEnabledCommand request, CancellationToken ct)
    {
        await access.EnsureAdminAsync(request.BudgetProfileId, request.UserId, ct);
        var profile = await profiles.SetCarryoverEnabledAsync(request.BudgetProfileId, request.Enabled, ct);
        return mapper.Map<BudgetProfileDto>(profile);
    }
}
