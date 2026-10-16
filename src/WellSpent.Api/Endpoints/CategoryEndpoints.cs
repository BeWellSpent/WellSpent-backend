using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Categories.CreateCategory;
using WellSpent.Application.Categories.DeleteCategory;
using WellSpent.Application.Categories.ListCategories;
using WellSpent.Application.Categories.UpdateCategory;

namespace WellSpent.Api.Endpoints;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/categories").WithTags("categories").RequireAuthorization();

        group.MapGet("", async (Guid? budgetProfileId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var categories = await sender.Send(new ListCategoriesQuery(CurrentUser.GetId(ctx), budgetProfileId), ct);
            return Results.Ok(new { categories });
        });

        group.MapPost("", async (CategoryRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var category = await sender.Send(new CreateCategoryCommand(CurrentUser.GetId(ctx), body.Name, body.Color), ct);
            return Results.Ok(new { category });
        });

        group.MapPut("/{id:int}", async (int id, CategoryRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var category = await sender.Send(new UpdateCategoryCommand(CurrentUser.GetId(ctx), id, body.Name, body.Color), ct);
            return Results.Ok(new { category });
        });

        group.MapDelete("/{id:int}", async (int id, int replacementId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteCategoryCommand(CurrentUser.GetId(ctx), id, replacementId), ct);
            return Results.Ok();
        });
    }

    private sealed record CategoryRequestBody(string Name, string Color);
}
