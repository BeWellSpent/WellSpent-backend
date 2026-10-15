using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.PaymentMethods.CreatePaymentMethod;
using WellSpent.Application.PaymentMethods.DeletePaymentMethod;
using WellSpent.Application.PaymentMethods.UpdatePaymentMethod;

namespace WellSpent.Api.Endpoints;

public static class PaymentMethodEndpoints
{
    /// <summary>
    /// Create/Update/Delete only — ListPaymentMethods is budget-scoped and
    /// lives on BudgetEndpoints' existing /rest/v1/budgets group instead of a
    /// second MapGroup("/rest/v1/budgets") here.
    /// </summary>
    public static void MapPaymentMethodEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/payment-methods").WithTags("payment-methods").RequireAuthorization();

        group.MapPost("", async (CreatePaymentMethodRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var method = await sender.Send(new CreatePaymentMethodCommand(
                CurrentUser.GetId(ctx), body.Name, body.Type, body.BudgetPersonId, body.Color), ct);
            return Results.Ok(new { method });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdatePaymentMethodRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var method = await sender.Send(new UpdatePaymentMethodCommand(
                CurrentUser.GetId(ctx), id, body.Name, body.Color, body.Alias ?? ""), ct);
            return Results.Ok(new { method });
        });

        group.MapDelete("/{id:guid}", async (Guid id, Guid replacementId, Guid budgetProfileId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeletePaymentMethodCommand(CurrentUser.GetId(ctx), id, replacementId, budgetProfileId), ct);
            return Results.Ok();
        });
    }

    private sealed record CreatePaymentMethodRequestBody(string Name, string Type, int BudgetPersonId, string Color);
    private sealed record UpdatePaymentMethodRequestBody(string Name, string Color, string? Alias);
}
