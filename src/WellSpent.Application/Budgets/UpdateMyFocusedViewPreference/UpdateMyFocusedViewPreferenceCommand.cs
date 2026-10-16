using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.UpdateMyFocusedViewPreference;

/// <summary>Personal setting — EnsureMemberAsync, not EnsureAdminAsync.</summary>
public sealed record UpdateMyFocusedViewPreferenceCommand(Guid UserId, Guid BudgetProfileId, bool Enabled)
    : IRequest<BudgetPersonDto>;

public sealed class UpdateMyFocusedViewPreferenceCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<UpdateMyFocusedViewPreferenceCommand, BudgetPersonDto>
{
    public async Task<BudgetPersonDto> Handle(UpdateMyFocusedViewPreferenceCommand request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var person = await profiles.UpdatePersonFocusedViewPreferenceAsync(request.BudgetProfileId, request.UserId, request.Enabled, ct);
        return mapper.Map<BudgetPersonDto>(person);
    }
}
