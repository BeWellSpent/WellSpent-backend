using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class CountryRepository(WellSpentDbContext db) : ICountryRepository
{
    public async Task<List<Country>> ListEnabledAsync(CancellationToken ct) =>
        await db.Countries.Where(c => c.IsEnabled).OrderBy(c => c.Name).ToListAsync(ct);

    public async Task<List<CountryFeature>> ListFeaturesAsync(CancellationToken ct) =>
        await db.CountryFeatures.OrderBy(f => f.CountryCode).ThenBy(f => f.FeatureName).ToListAsync(ct);
}
