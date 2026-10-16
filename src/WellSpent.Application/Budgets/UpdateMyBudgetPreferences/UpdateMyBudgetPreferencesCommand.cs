using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.UpdateMyBudgetPreferences;

/// <summary>
/// Writes the caller's own presentation settings. No role check: a Viewer
/// may still choose how they look at the budget. Matched on (profile, userId)
/// in the repository, so this cannot reach another member — which is also
/// why this command takes no person id.
/// </summary>
public sealed record UpdateMyBudgetPreferencesCommand(Guid UserId, Guid BudgetProfileId, string? PlanChartType, string? OverviewChartType)
    : IRequest<BudgetPersonDto>;

public sealed class UpdateMyBudgetPreferencesCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<UpdateMyBudgetPreferencesCommand, BudgetPersonDto>
{
    public async Task<BudgetPersonDto> Handle(UpdateMyBudgetPreferencesCommand request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var person = await profiles.UpdatePersonPreferencesAsync(
            request.BudgetProfileId, request.UserId, request.PlanChartType, request.OverviewChartType, ct);
        return mapper.Map<BudgetPersonDto>(person);
    }
}
