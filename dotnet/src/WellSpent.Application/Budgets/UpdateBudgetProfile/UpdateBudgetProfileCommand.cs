using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.UpdateBudgetProfile;

public sealed record UpdateBudgetProfileCommand(Guid UserId, Guid Id, string Name, string Cycle)
    : IRequest<BudgetProfileDto>;

public sealed class UpdateBudgetProfileCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<UpdateBudgetProfileCommand, BudgetProfileDto>
{
    public async Task<BudgetProfileDto> Handle(UpdateBudgetProfileCommand request, CancellationToken ct)
    {
        await access.EnsureAdminAsync(request.Id, request.UserId, ct);
        var profile = await profiles.UpdateAsync(request.Id, request.Name, request.Cycle, ct);
        return mapper.Map<BudgetProfileDto>(profile);
    }
}
