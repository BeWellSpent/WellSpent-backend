using WellSpent.Application.Common;
using MediatR;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets.AddIncomeSource;

public sealed record AddIncomeSourceCommand(
    Guid UserId, Guid BudgetProfileId, string Name, string IncomeType, Money DefaultAmount,
    bool Recurring, int? BudgetPersonId, string PaymentFrequency, bool BeforeTax)
    : IRequest<IncomeSourceDto>;

public sealed class AddIncomeSourceCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, IUserRepository users, TaxReserveRecalculator taxReserve)
    : IRequestHandler<AddIncomeSourceCommand, IncomeSourceDto>
{
    public async Task<IncomeSourceDto> Handle(AddIncomeSourceCommand request, CancellationToken ct)
    {
        var profile = await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        // Free tier: max 2 income sources per person.
        var owner = await users.GetByIdAsync(profile.UserId, ct);
        if (owner.Plan == "free")
        {
            var existing = await profiles.ListIncomeSourcesAsync(request.BudgetProfileId, ct);
            var count = existing.Count(src => src.BudgetPersonId == request.BudgetPersonId);
            if (count >= 2)
            {
                throw new AppValidationException("free tier: income sources are limited to 2 per person; upgrade to Pro for unlimited");
            }
        }

        var source = await profiles.AddIncomeSourceAsync(new IncomeSource
        {
            BudgetProfileId = request.BudgetProfileId,
            BudgetPersonId = request.BudgetPersonId,
            Name = request.Name,
            IncomeType = request.IncomeType,
            DefaultAmount = request.DefaultAmount.ToDecimal(),
            Recurring = request.Recurring,
            PaymentFrequency = request.PaymentFrequency,
            BeforeTax = request.BeforeTax,
        }, ct);

        await taxReserve.RecalculateAsync(request.BudgetProfileId, ct);

        return IncomeSourceMapping.ToDto(source);
    }
}
