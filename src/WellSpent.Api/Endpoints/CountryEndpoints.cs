using MediatR;
using WellSpent.Application.Countries.ListCountries;

namespace WellSpent.Api.Endpoints;

public static class CountryEndpoints
{
    public static void MapCountryEndpoints(this IEndpointRouteBuilder app)
    {
        // Public, global, rarely-changing — three rows that change roughly
        // never. Same path/shape as the existing Go REST endpoint.
        app.MapGet("/rest/v1/countries", async (ISender sender, CancellationToken ct) =>
        {
            var countries = await sender.Send(new ListCountriesQuery(), ct);
            return Results.Ok(new { countries });
        }).WithTags("reference");
    }
}
