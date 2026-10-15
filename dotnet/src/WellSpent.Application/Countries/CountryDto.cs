namespace WellSpent.Application.Countries;

public sealed record CountryFeatureDto(string Name, bool IsEnabled);

public sealed record CountryDto(string Code, string Name, bool IsEnabled, List<CountryFeatureDto> Features);
