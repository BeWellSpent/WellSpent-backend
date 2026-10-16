using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Countries.ListCountries;

/// <summary>Public — no auth required. Mirrors Go's UserService.ListCountries: fetch both tables, merge features by country code.</summary>
public sealed record ListCountriesQuery : IRequest<List<CountryDto>>;

public sealed class ListCountriesQueryHandler(ICountryRepository countries)
    : IRequestHandler<ListCountriesQuery, List<CountryDto>>
{
    public async Task<List<CountryDto>> Handle(ListCountriesQuery request, CancellationToken ct)
    {
        var rows = await countries.ListEnabledAsync(ct);
        var features = await countries.ListFeaturesAsync(ct);

        var byCountry = features
            .GroupBy(f => f.CountryCode)
            .ToDictionary(g => g.Key, g => g.Select(f => new CountryFeatureDto(f.FeatureName, f.IsEnabled)).ToList());

        return rows.Select(c => new CountryDto(
            c.Code, c.Name, c.IsEnabled,
            byCountry.TryGetValue(c.Code, out var f) ? f : [])).ToList();
    }
}
