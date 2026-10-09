using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.UpdateBudgetPerson;

public sealed record UpdateBudgetPersonCommand(Guid UserId, Guid BudgetProfileId, int PersonId, string Color)
    : IRequest<BudgetPersonDto>;

public sealed class UpdateBudgetPersonCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles, IMapper mapper)
    : IRequestHandler<UpdateBudgetPersonCommand, BudgetPersonDto>
{
    public async Task<BudgetPersonDto> Handle(UpdateBudgetPersonCommand request, CancellationToken ct)
    {
        await access.EnsureAdminAsync(request.BudgetProfileId, request.UserId, ct);
        var person = await profiles.UpdatePersonColorAsync(request.PersonId, request.BudgetProfileId, request.Color, ct);
        return mapper.Map<BudgetPersonDto>(person);
    }
}
