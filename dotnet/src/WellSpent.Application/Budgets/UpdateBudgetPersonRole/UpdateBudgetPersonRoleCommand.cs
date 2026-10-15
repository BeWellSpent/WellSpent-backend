using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.UpdateBudgetPersonRole;

public sealed record UpdateBudgetPersonRoleCommand(Guid UserId, Guid BudgetProfileId, int PersonId, string Role)
    : IRequest<BudgetPersonDto>;

public sealed class UpdateBudgetPersonRoleCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<UpdateBudgetPersonRoleCommand, BudgetPersonDto>
{
    public async Task<BudgetPersonDto> Handle(UpdateBudgetPersonRoleCommand request, CancellationToken ct)
    {
        await access.EnsureAdminAsync(request.BudgetProfileId, request.UserId, ct);
        var person = await profiles.UpdatePersonRoleAsync(request.PersonId, request.BudgetProfileId, request.Role, ct);
        return mapper.Map<BudgetPersonDto>(person);
    }
}
