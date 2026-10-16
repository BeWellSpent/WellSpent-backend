using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Budgets.UpdateIncomeEntry;

/// <summary>Only the amount is editable per period; source metadata is the source of truth.</summary>
public sealed record UpdateIncomeEntryCommand(Guid UserId, int Id, Guid BudgetPeriodId, Money Amount)
    : IRequest<IncomeEntryDto>;

public sealed class UpdateIncomeEntryCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles)
    : IRequestHandler<UpdateIncomeEntryCommand, IncomeEntryDto>
{
    public async Task<IncomeEntryDto> Handle(UpdateIncomeEntryCommand request, CancellationToken ct)
    {
        // Mirrors Go's assertPeriodCollaborator: archived check comes before
        // the role check, so a non-collaborator on an archived period sees
        // the archived error, not a forbidden one.
        var period = await profiles.GetPeriodByIdAsync(request.BudgetPeriodId, ct);
        if (period.IsArchived)
        {
            throw new WellSpent.Domain.Exceptions.ForbiddenException("this budget period is archived and read-only");
        }
        await access.EnsureCollaboratorOrAboveAsync(period.BudgetProfileId, request.UserId, ct);

        var entry = await profiles.UpdateIncomeEntryAsync(request.Id, request.BudgetPeriodId, request.Amount.ToDecimal(), ct);
        return IncomeEntryMapping.ToDto(entry);
    }
}
