using WellSpent.Domain.Entities;

namespace WellSpent.Domain.Abstractions;

public interface ICountryRepository
{
    Task<List<Country>> ListEnabledAsync(CancellationToken ct);

    Task<List<CountryFeature>> ListFeaturesAsync(CancellationToken ct);
}
