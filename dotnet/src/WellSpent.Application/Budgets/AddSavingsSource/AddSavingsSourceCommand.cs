using MediatR;
using Microsoft.Extensions.Logging;
using WellSpent.Application.Budgets;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets.AddSavingsSource;

public sealed record AddSavingsSourceCommand(
    Guid UserId, Guid BudgetProfileId, string Name, Money Amount, Guid? PaymentMethodId, int[] PaymentDays)
    : IRequest<SavingsSourceDto>;

/// <summary>Mirrors Go's AddSavingsSource, including the Fixed/Savings transaction it spawns in the current period via createSavingsTransactions.</summary>
public sealed class AddSavingsSourceCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, ITransactionRepository transactions,
    ILogger<AddSavingsSourceCommandHandler> logger)
    : IRequestHandler<AddSavingsSourceCommand, SavingsSourceDto>
{
    public async Task<SavingsSourceDto> Handle(AddSavingsSourceCommand request, CancellationToken ct)
    {
        await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var n = request.PaymentDays.Length;
        if (n is not (1 or 2 or 4))
        {
            throw new AppValidationException("payment_days must have 1, 2, or 4 entries");
        }

        int? personId = null;
        if (request.PaymentMethodId is { } pmId)
        {
            personId = await profiles.GetPaymentMethodBudgetPersonIdAsync(pmId, ct);
        }

        var source = await profiles.AddSavingsSourceAsync(new SavingsSource
        {
            BudgetProfileId = request.BudgetProfileId,
            BudgetPersonId = personId,
            Name = request.Name,
            Amount = request.Amount.ToDecimal(),
            Frequency = PaymentDaysFrequency(n),
            PaymentMethodId = request.PaymentMethodId,
            PaymentDays = request.PaymentDays,
        }, ct);

        await SavingsTransactionSpawning.SpawnAsync(profiles, transactions, logger, request.BudgetProfileId, source, ct);

        return SavingsSourceMapping.ToDto(source);
    }

    internal static string PaymentDaysFrequency(int n) => n switch
    {
        2 => "bi_weekly",
        4 => "weekly",
        _ => "monthly",
    };
}
