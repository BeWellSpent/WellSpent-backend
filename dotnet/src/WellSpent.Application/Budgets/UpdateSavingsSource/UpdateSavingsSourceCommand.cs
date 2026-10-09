using MediatR;
using WellSpent.Application.Budgets.AddSavingsSource;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets.UpdateSavingsSource;

public sealed record UpdateSavingsSourceCommand(
    Guid UserId, int Id, Guid BudgetProfileId, string Name, Money Amount, Guid? PaymentMethodId, int[] PaymentDays)
    : IRequest<SavingsSourceDto>;

/// <summary>
/// Mirrors Go's UpdateSavingsSource, including its own quirk: when
/// PaymentDays is empty, Go's comment says "preserve existing frequency" but
/// the SQL it builds (UpdateSavingsSourceParams.Frequency = "") actually
/// overwrites the column with an empty string — the comment and the code
/// disagree, and this mirrors the code. Deferred, same as AddSavingsSource:
/// deleting the old auto-created transaction and spawning a new one
/// (B5 batch 4/5) — the row itself is fully functional today.
/// </summary>
public sealed class UpdateSavingsSourceCommandHandler(BudgetAccessGuard access, IBudgetProfileRepository profiles)
    : IRequestHandler<UpdateSavingsSourceCommand, SavingsSourceDto>
{
    public async Task<SavingsSourceDto> Handle(UpdateSavingsSourceCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var n = request.PaymentDays.Length;
        if (n is not (0 or 1 or 2 or 4))
        {
            throw new AppValidationException("payment_days must have 1, 2, or 4 entries");
        }

        // Throws NotFoundException if missing — not otherwise used; Go reads
        // it only to find the old payment method for the deferred
        // transaction-cleanup hook.
        await profiles.GetSavingsSourceAsync(request.Id, request.BudgetProfileId, ct);

        int? personId = null;
        if (request.PaymentMethodId is { } pmId)
        {
            personId = await profiles.GetPaymentMethodBudgetPersonIdAsync(pmId, ct);
        }

        var freq = n == 0 ? "" : AddSavingsSourceCommandHandler.PaymentDaysFrequency(n);

        var updated = await profiles.UpdateSavingsSourceAsync(new SavingsSource
        {
            Id = request.Id,
            BudgetProfileId = request.BudgetProfileId,
            Name = request.Name,
            Amount = request.Amount.ToDecimal(),
            Frequency = freq,
            BudgetPersonId = personId,
            PaymentMethodId = request.PaymentMethodId,
            PaymentDays = request.PaymentDays,
        }, ct);

        // HOOK: delete the old auto-created transaction and spawn a new one
        // reflecting the updated values (B5 batch 4/5).

        return SavingsSourceMapping.ToDto(updated);
    }
}
