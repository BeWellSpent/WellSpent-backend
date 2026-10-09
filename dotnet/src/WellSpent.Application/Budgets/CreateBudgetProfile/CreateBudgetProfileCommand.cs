using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Users;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets.CreateBudgetProfile;

public sealed record CreateBudgetProfileCommand(Guid UserId, string Name, string Cycle)
    : IRequest<CreateBudgetProfileResult>;

public sealed record CreateBudgetProfileResult(BudgetProfileDto Profile, BudgetPeriodDto? Period);

public sealed class CreateBudgetProfileCommandHandler(
    IBudgetProfileRepository profiles, IUserRepository users, IMapper mapper,
    ILogger<CreateBudgetProfileCommandHandler> logger)
    : IRequestHandler<CreateBudgetProfileCommand, CreateBudgetProfileResult>
{
    public async Task<CreateBudgetProfileResult> Handle(CreateBudgetProfileCommand request, CancellationToken ct)
    {
        // Only one owned budget profile per user, regardless of plan tier — a
        // user can still be a *member* of other people's shared budgets
        // without limit; this only caps how many they can own.
        var owned = await profiles.ListByUserIdAsync(request.UserId, ct);
        if (owned.Count > 0)
        {
            throw new AppValidationException("you already have a budget — only one budget profile is allowed per account");
        }

        if (await profiles.ExistsByNameAndUserAsync(request.Name, request.UserId, ct))
        {
            throw new DuplicateException("budget_profile", "name", request.Name);
        }

        var owner = await users.GetByIdAsync(request.UserId, ct);
        var profile = await profiles.CreateAsync(new BudgetProfile
        {
            UserId = request.UserId,
            Name = request.Name,
            Cycle = request.Cycle,
            CountryCode = owner.CountryCode,
        }, ct);

        // Auto-add budget owner as the first person on the profile.
        try
        {
            await profiles.AddPersonAsync(new BudgetPerson
            {
                BudgetProfileId = profile.Id,
                UserName = UserDisplayRules.DisplayName(owner),
                UserId = request.UserId,
                Color = "",
                Role = "admin",
            }, ct);
        }
        catch (Exception ex)
        {
            // The owner isn't on their own budget: no payment methods can be
            // attributed and the people list opens empty.
            logger.LogError(ex, "budget_profile.create.add_owner_failed profile_id={ProfileId} user_id={UserId}", profile.Id, request.UserId);
        }

        // Create the first period immediately. Non-fatal: profile was
        // created, period creation failed.
        BudgetPeriod? period = null;
        try
        {
            period = await BudgetPeriodRollover.CreateNextPeriodAsync(profiles, profile, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "budget_profile.create.first_period_failed profile_id={ProfileId}", profile.Id);
        }

        return new CreateBudgetProfileResult(mapper.Map<BudgetProfileDto>(profile), period is null ? null : mapper.Map<BudgetPeriodDto>(period));
    }
}
