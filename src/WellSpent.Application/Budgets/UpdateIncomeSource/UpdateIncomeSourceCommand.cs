using MediatR;
using WellSpent.Application.Common;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using WellSpent.Domain.Exceptions;

namespace WellSpent.Application.Budgets.UpdateIncomeSource;

public sealed record UpdateIncomeSourceCommand(
    Guid UserId, int Id, Guid BudgetProfileId, string Name, string IncomeType, Money DefaultAmount,
    bool Recurring, int? BudgetPersonId, string PaymentFrequency, bool BeforeTax,
    DateOnly? AnchorDate = null, int EarlyArrivalToleranceDays = 0, bool AdjustForWeekends = true)
    : IRequest<IncomeSourceDto>;

/// <summary>
/// Full-replace update. Renaming/retuning the amount etc. mutates the row in
/// place, but changing the pay schedule (AnchorDate/EarlyArrivalToleranceDays)
/// forks a new row instead — the old one is capped at the next predicted
/// period boundary so past periods keep reading the schedule that was
/// actually in force, rather than retroactively rewriting history.
/// </summary>
public sealed class UpdateIncomeSourceCommandHandler(
    BudgetAccessGuard access, IBudgetProfileRepository profiles, TaxReserveRecalculator taxReserve)
    : IRequestHandler<UpdateIncomeSourceCommand, IncomeSourceDto>
{
    public async Task<IncomeSourceDto> Handle(UpdateIncomeSourceCommand request, CancellationToken ct)
    {
        var profile = await access.EnsureCollaboratorOrAboveAsync(request.BudgetProfileId, request.UserId, ct);

        var allSources = await profiles.ListIncomeSourcesAsync(request.BudgetProfileId, ct) ?? [];
        var existing = allSources.FirstOrDefault(s => s.Id == request.Id)
            ?? throw new NotFoundException("income_source", request.Id.ToString());

        var scheduleChanged = existing.AnchorDate != request.AnchorDate
            || existing.EarlyArrivalToleranceDays != request.EarlyArrivalToleranceDays
            || existing.AdjustForWeekends != request.AdjustForWeekends;

        BudgetPeriod? currentPeriod = null;
        if (scheduleChanged)
        {
            try
            {
                currentPeriod = await profiles.GetLatestPeriodAsync(request.BudgetProfileId, ct);
            }
            catch (NotFoundException)
            {
                // No period yet to fork against — update in place below instead.
            }
        }

        IncomeSource source;
        if (currentPeriod is null)
        {
            source = await UpdateInPlaceAsync(request, ct);
        }
        else
        {
            var predictedNextStart = IncomePeriodDates.ComputeNext(allSources, profile.Cycle, currentPeriod.StartDate, currentPeriod.EndDate).Start;
            await profiles.SetIncomeSourceEffectiveToAsync(existing.Id, predictedNextStart, ct);

            source = await profiles.AddIncomeSourceAsync(new IncomeSource
            {
                BudgetProfileId = request.BudgetProfileId,
                Name = request.Name,
                IncomeType = request.IncomeType,
                DefaultAmount = request.DefaultAmount.ToDecimal(),
                Recurring = request.Recurring,
                BudgetPersonId = request.BudgetPersonId,
                PaymentFrequency = request.PaymentFrequency,
                BeforeTax = request.BeforeTax,
                AnchorDate = request.AnchorDate,
                EarlyArrivalToleranceDays = request.EarlyArrivalToleranceDays,
                AdjustForWeekends = request.AdjustForWeekends,
                EffectiveFrom = predictedNextStart,
                EffectiveTo = null,
            }, ct);
        }

        await taxReserve.RecalculateAsync(request.BudgetProfileId, ct);

        return IncomeSourceMapping.ToDto(source);
    }

    private async Task<IncomeSource> UpdateInPlaceAsync(UpdateIncomeSourceCommand request, CancellationToken ct) =>
        await profiles.UpdateIncomeSourceAsync(new IncomeSource
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
            AnchorDate = request.AnchorDate,
            EarlyArrivalToleranceDays = request.EarlyArrivalToleranceDays,
            AdjustForWeekends = request.AdjustForWeekends,
        }, ct);
}
