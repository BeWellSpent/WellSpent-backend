using Microsoft.Extensions.Logging;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Application.Budgets;

/// <summary>
/// Recomputes per-person US tax-reserve savings entries whenever income
/// sources change. Mirrors Go's recalculateTaxReserve exactly, including its
/// best-effort posture: every failure here is logged and swallowed, never
/// propagated, because this is always a secondary effect of a request (adding
/// an income source, rolling a period forward) whose primary write already
/// succeeded.
/// </summary>
public sealed class TaxReserveRecalculator(
    IBudgetProfileRepository profiles, IUserRepository users, ILogger<TaxReserveRecalculator> logger)
{
    public async Task RecalculateAsync(Guid profileId, CancellationToken ct)
    {
        BudgetProfile profile;
        try
        {
            profile = await profiles.GetByIdAsync(profileId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "tax_reserve.load_profile_failed profile_id={ProfileId}", profileId);
            return;
        }

        var countryCode = profile.CountryCode;
        if (string.IsNullOrEmpty(countryCode))
        {
            try
            {
                var owner = await users.GetByIdAsync(profile.UserId, ct);
                countryCode = owner.CountryCode;
            }
            catch (Exception)
            {
                // Fall through — countryCode stays null, resolved as not-US below.
            }
        }
        if (countryCode != "US")
        {
            return;
        }

        List<IncomeSource> sources;
        List<BudgetPerson> people;
        try
        {
            sources = await profiles.ListIncomeSourcesAsync(profileId, ct);
            people = await profiles.ListPeopleAsync(profileId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "tax_reserve.load_sources_or_people_failed profile_id={ProfileId}", profileId);
            return;
        }

        int? ownerPersonId = null;
        var userByPerson = new Dictionary<int, Guid>();
        foreach (var p in people)
        {
            if (p.UserId is { } uid)
            {
                userByPerson[p.Id] = uid;
                if (uid == profile.UserId)
                {
                    ownerPersonId = p.Id;
                }
            }
        }

        // Group annual before-tax income by person. Unattributed income
        // (person 0) falls back to the owner's person entry.
        var incomeByPerson = new Dictionary<int, decimal>();
        foreach (var src in sources)
        {
            if (!src.BeforeTax) continue;
            var annual = src.DefaultAmount * 12;
            var pid = src.BudgetPersonId ?? 0;
            if (pid == 0 && ownerPersonId is { } owner)
            {
                pid = owner;
            }
            incomeByPerson[pid] = incomeByPerson.GetValueOrDefault(pid) + annual;
        }

        try
        {
            await profiles.DeleteTaxReserveSavingsSourceAsync(profileId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "tax_reserve.clear_existing_failed profile_id={ProfileId}", profileId);
        }

        if (incomeByPerson.Count == 0)
        {
            return;
        }

        User ownerUser;
        try
        {
            ownerUser = await users.GetByIdAsync(profile.UserId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "tax_reserve.load_owner_failed profile_id={ProfileId}", profileId);
            return;
        }
        var ownerState = ownerUser.StateCode ?? "";
        var ownerFilingStatus = int.TryParse(ownerUser.FilingStatus, out var fs) ? fs : 0;

        foreach (var (personId, annualIncome) in incomeByPerson)
        {
            if (annualIncome == 0) continue;

            var stateCode = ownerState;
            var filingStatus = ownerFilingStatus;
            if (userByPerson.TryGetValue(personId, out var linkedUserId))
            {
                try
                {
                    var linkedUser = await users.GetByIdAsync(linkedUserId, ct);
                    if (!string.IsNullOrEmpty(linkedUser.StateCode)) stateCode = linkedUser.StateCode;
                    if (!string.IsNullOrEmpty(linkedUser.FilingStatus) && int.TryParse(linkedUser.FilingStatus, out var linkedFs))
                    {
                        filingStatus = linkedFs;
                    }
                }
                catch (Exception)
                {
                    // Falls back to the owner's own tax settings.
                }
            }

            var estimate = TaxEstimation.Estimate(annualIncome, stateCode, filingStatus);
            try
            {
                await profiles.UpsertTaxReserveSavingsSourceAsync(
                    profileId, personId, ToMonthly(estimate.TotalAnnual), ToMonthly(estimate.FederalTax), ToMonthly(estimate.StateTax), ct);
            }
            catch (Exception ex)
            {
                // The person's tax reserve silently stops tracking their income.
                logger.LogError(ex, "tax_reserve.upsert_failed profile_id={ProfileId} person_id={PersonId}", profileId, personId);
            }
        }
    }

    private static decimal ToMonthly(decimal annual) => Math.Round(annual / 12, 2, MidpointRounding.AwayFromZero);
}
