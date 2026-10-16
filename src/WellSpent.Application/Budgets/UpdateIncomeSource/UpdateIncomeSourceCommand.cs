using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Budgets.UpdateIncomeSource;

public sealed record UpdateIncomeSourceCommand(
    Guid UserId, int Id, Guid BudgetProfileId, string Name, string IncomeType, Money DefaultAmount,
    bool Recurring, int? BudgetPersonId, string PaymentFrequency, bool BeforeTax)
    : IRequest<IncomeSourceDto>;

public sealed class UpdateIncomeSourceCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, TaxReserveRecalculator taxReserve)
    : IRequestHandler<UpdateIncomeSourceCommand, IncomeSourceDto>
{
    public async Task<IncomeSourceDto> Handle(UpdateIncomeSourceCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var source = await profiles.UpdateIncomeSourceAsync(new IncomeSource
        {
            Id = request.Id,
            BudgetProfileId = request.BudgetProfileId,
            Name = request.Name,
            IncomeType = request.IncomeType,
            DefaultAmount = request.DefaultAmount.ToDecimal(),
            Recurring = request.Recurring,
            BudgetPersonId = request.BudgetPersonId,
            PaymentFrequency = request.PaymentFrequency,
            BeforeTax = request.BeforeTax,
        }, ct);

        await taxReserve.RecalculateAsync(request.BudgetProfileId, ct);

        return IncomeSourceMapping.ToDto(source);
    }
}
