using AutoMapper;
using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets.AddBudgetPeople;

public sealed record NewBudgetPersonInput(string UserName, Guid? UserId, string Color);

public sealed record AddBudgetPeopleCommand(Guid UserId, Guid BudgetProfileId, List<NewBudgetPersonInput> People)
    : IRequest<List<BudgetPersonDto>>;

public sealed class AddBudgetPeopleCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, IUserRepository users, IMapper mapper)
    : IRequestHandler<AddBudgetPeopleCommand, List<BudgetPersonDto>>
{
    public async Task<List<BudgetPersonDto>> Handle(AddBudgetPeopleCommand request, CancellationToken ct)
    {
        var profile = await access.EnsureAdminAsync(request.BudgetProfileId, request.UserId, ct);

        // Free tier: max 2 active people per budget.
        var owner = await users.GetByIdAsync(profile.UserId, ct);
        if (owner.Plan == "free")
        {
            var existing = await profiles.ListPeopleAsync(request.BudgetProfileId, ct);
            if (existing.Count + request.People.Count > 2)
            {
                throw new AppValidationException("free tier: budget is limited to 2 people; upgrade to Pro for unlimited members");
            }
        }

        var results = new List<BudgetPerson>();
        foreach (var p in request.People)
        {
            // Country constraint: if the person being added is a registered
            // user, they must be in the same country as the budget profile.
            if (p.UserId is { } pid)
            {
                var person = await users.GetByIdAsync(pid, ct);
                if (profile.CountryCode is not null && person.CountryCode is not null && person.CountryCode != profile.CountryCode)
                {
                    throw new AppValidationException("all budget members must be in the same country");
                }
            }

            if (await profiles.ExistsPersonAsync(request.BudgetProfileId, p.UserName, ct))
            {
                throw new DuplicateException("person", "name", p.UserName);
            }

            var role = p.UserId is not null ? "collaborator" : "unspecified";
            var added = await profiles.AddPersonAsync(new BudgetPerson
            {
                BudgetProfileId = request.BudgetProfileId,
                UserName = p.UserName,
                UserId = p.UserId,
                Color = p.Color,
                Role = role,
            }, ct);
            results.Add(added);
        }

        return mapper.Map<List<BudgetPersonDto>>(results);
    }
}
