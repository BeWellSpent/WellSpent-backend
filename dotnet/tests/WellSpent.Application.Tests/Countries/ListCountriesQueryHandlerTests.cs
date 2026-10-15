using NSubstitute;
using WellSpent.Application.Countries.ListCountries;
using WellSpent.Domain.Abstractions;
using WellSpent.Domain.Entities;
using Xunit;

namespace WellSpent.Application.Tests.Countries;

public sealed class ListCountriesQueryHandlerTests
{
    private readonly ICountryRepository _countries = Substitute.For<ICountryRepository>();

    [Fact]
    public async Task MergesFeaturesByCountryCode()
    {
        _countries.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns([
            new Country { Code = "US", Name = "United States" },
            new Country { Code = "AR", Name = "Argentina" },
        ]);
        _countries.ListFeaturesAsync(Arg.Any<CancellationToken>()).Returns([
            new CountryFeature { CountryCode = "US", FeatureName = "before_tax_income", IsEnabled = true },
        ]);

        var result = await new ListCountriesQueryHandler(_countries).Handle(new ListCountriesQuery(), CancellationToken.None);

        var us = result.Single(c => c.Code == "US");
        Assert.Equal([new("before_tax_income", true)], us.Features);

        var ar = result.Single(c => c.Code == "AR");
        Assert.Empty(ar.Features);
    }
}
