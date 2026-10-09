using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.UpdateMyManualMatchReviewPreference;

/// <summary>Personal setting — EnsureMemberAsync, not EnsureAdminAsync.</summary>
public sealed record UpdateMyManualMatchReviewPreferenceCommand(Guid UserId, Guid BudgetProfileId, bool Enabled)
    : IRequest<BudgetPersonDto>;

public sealed class UpdateMyManualMatchReviewPreferenceCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<UpdateMyManualMatchReviewPreferenceCommand, BudgetPersonDto>
{
    public async Task<BudgetPersonDto> Handle(UpdateMyManualMatchReviewPreferenceCommand request, CancellationToken ct)
    {
        await access.EnsureMemberAsync(request.BudgetProfileId, request.UserId, ct);
        var person = await profiles.UpdatePersonManualMatchReviewPreferenceAsync(request.BudgetProfileId, request.UserId, request.Enabled, ct);
        return mapper.Map<BudgetPersonDto>(person);
    }
}
