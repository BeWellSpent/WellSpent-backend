using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Budgets;
using WellSpent.Application.Budgets.AddSavingsSource;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets.UpdateSavingsSource;

public sealed record UpdateSavingsSourceCommand(
    Guid UserId, int Id, Guid BudgetProfileId, string Name, Money Amount, Guid? PaymentMethodId, int[] PaymentDays)
    : IRequest<SavingsSourceDto>;

/// <summary>Mirrors Go's UpdateSavingsSource, including its quirk: empty PaymentDays writes an empty-string frequency, not "preserve existing" as Go's own comment claims.</summary>
public sealed class UpdateSavingsSourceCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions,
    ILogger<UpdateSavingsSourceCommandHandler> logger)
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

        var old = await profiles.GetSavingsSourceAsync(request.Id, request.BudgetProfileId, ct);

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

        if (old.PaymentMethodId is not null)
        {
            await SavingsTransactionSpawning.DeleteExistingTransactionsAsync(transactions, logger, request.BudgetProfileId, old, ct);
        }
        if (updated.PaymentMethodId is not null && updated.PaymentDays.Length > 0)
        {
            await SavingsTransactionSpawning.SpawnAsync(profiles, transactions, logger, request.BudgetProfileId, updated, ct);
        }

        return SavingsSourceMapping.ToDto(updated);
    }
}
