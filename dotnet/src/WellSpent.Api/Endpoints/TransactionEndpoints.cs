using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Common;
using WellSpent.Application.FixedExpenses.CreateFixedExpenseFromTransaction;
using WellSpent.Application.FixedExpenses.CreateInstallmentPlan;
using WellSpent.Application.FixedExpenses.DeleteInstallmentPlan;
using WellSpent.Application.Transactions.CreateTransaction;
using WellSpent.Application.Transactions.DeleteTransaction;
using WellSpent.Application.Transactions.ListTransactions;
using WellSpent.Application.Transactions.MarkTransactionAsPaid;
using WellSpent.Application.Transactions.SetTransactionExcluded;
using WellSpent.Application.Transactions.UnmarkTransactionAsPaid;
using WellSpent.Application.Transactions.UpdateTransaction;
using WellSpent.Application.TransactionReviews.ConfirmTransactionReview;
using WellSpent.Application.TransactionReviews.DismissTransactionReview;
using WellSpent.Application.TransactionReviews.MarkTransactionForReview;

namespace WellSpent.Api.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/transactions").WithTags("transactions").RequireAuthorization();

        group.MapGet("", async (
            Guid budgetPeriodId, int? categoryId, string? transactionType, bool? focusedView,
            HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListTransactionsQuery(
                CurrentUser.GetId(ctx), budgetPeriodId, categoryId, transactionType, focusedView ?? false), ct);
            return Results.Ok(new { transactions = result });
        });

        group.MapPost("", async (CreateTransactionRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var transaction = await sender.Send(new CreateTransactionCommand(
                CurrentUser.GetId(ctx), body.Name, body.Amount, body.PlannedAmount, body.Date, body.RenewalDate,
                body.BudgetPeriodId, body.CategoryId, body.PaymentMethodId, body.TransactionFrequency, body.TransactionType), ct);
            return Results.Ok(new { transaction });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateTransactionRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var transaction = await sender.Send(new UpdateTransactionCommand(
                CurrentUser.GetId(ctx), id, body.Name, body.Amount, body.PlannedAmount, body.Date,
                body.CategoryId, body.PaymentMethodId, body.TransactionFrequency, body.TransactionType), ct);
            return Results.Ok(new { transaction });
        });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteTransactionCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok();
        });

        group.MapPost("/{id:guid}/mark-paid", async (Guid id, MarkPaidRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var transaction = await sender.Send(new MarkTransactionAsPaidCommand(
                CurrentUser.GetId(ctx), id, body.BudgetPeriodId, body.PaidAmount, body.PaidDate), ct);
            return Results.Ok(new { transaction });
        });

        group.MapPost("/{id:guid}/unmark-paid", async (Guid id, BudgetPeriodRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var transaction = await sender.Send(new UnmarkTransactionAsPaidCommand(CurrentUser.GetId(ctx), id, body.BudgetPeriodId), ct);
            return Results.Ok(new { transaction });
        });

        group.MapPut("/{id:guid}/excluded", async (Guid id, SetExcludedRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var transaction = await sender.Send(new SetTransactionExcludedCommand(
                CurrentUser.GetId(ctx), id, body.BudgetPeriodId, body.Excluded), ct);
            return Results.Ok(new { transaction });
        });

        group.MapPost("/{id:guid}/installment-plan", async (
            Guid id, Guid budgetPeriodId, CreateInstallmentPlanRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateInstallmentPlanCommand(
                CurrentUser.GetId(ctx), id, budgetPeriodId, body.FirstPaymentDate, body.TotalPayments, body.EndDate), ct);
            return Results.Ok(new { fixedExpense = result.Expense, transaction = result.Transaction });
        });

        group.MapDelete("/{id:guid}/installment-plan", async (Guid id, Guid budgetPeriodId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var transaction = await sender.Send(new DeleteInstallmentPlanCommand(CurrentUser.GetId(ctx), id, budgetPeriodId), ct);
            return Results.Ok(new { transaction });
        });

        group.MapPost("/{id:guid}/flag-for-review", async (Guid id, FlagForReviewRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var review = await sender.Send(new MarkTransactionForReviewCommand(
                CurrentUser.GetId(ctx), id, body.MatchedTransactionId, body.BudgetProfileId), ct);
            return Results.Ok(new { review });
        });

        group.MapPost("/{id:guid}/fixed-expense", async (
            Guid id, Guid budgetPeriodId, CreateFixedExpenseFromTransactionRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateFixedExpenseFromTransactionCommand(
                CurrentUser.GetId(ctx), id, budgetPeriodId, body.Name ?? "", body.AnchorDate, body.FrequencyUnit,
                body.IntervalMonths, body.IntervalWeeks, body.DayOfWeek), ct);
            return Results.Ok(new { fixedExpense = result.Expense, transaction = result.Transaction });
        });

        // Separate namespace — reviews are addressed by their own id, not nested under a transaction.
        var reviewGroup = app.MapGroup("/rest/v1/transaction-reviews").WithTags("transactions").RequireAuthorization();

        reviewGroup.MapPost("/{id:guid}/confirm", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ConfirmTransactionReviewCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok();
        });

        reviewGroup.MapPost("/{id:guid}/dismiss", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DismissTransactionReviewCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok();
        });
    }

    private sealed record CreateTransactionRequestBody(
        string? Name, Money Amount, Money PlannedAmount, DateOnly? Date, DateOnly? RenewalDate,
        Guid? BudgetPeriodId, int? CategoryId, Guid? PaymentMethodId, string? TransactionFrequency, string? TransactionType);

    private sealed record UpdateTransactionRequestBody(
        string? Name, Money Amount, Money PlannedAmount, DateOnly? Date,
        int? CategoryId, Guid? PaymentMethodId, string? TransactionFrequency, string? TransactionType);

    private sealed record MarkPaidRequestBody(Guid BudgetPeriodId, Money PaidAmount, DateOnly PaidDate);
    private sealed record BudgetPeriodRequestBody(Guid BudgetPeriodId);
    private sealed record SetExcludedRequestBody(Guid BudgetPeriodId, bool Excluded);
    private sealed record CreateInstallmentPlanRequestBody(DateOnly FirstPaymentDate, int TotalPayments, DateOnly? EndDate);
    private sealed record FlagForReviewRequestBody(Guid MatchedTransactionId, Guid BudgetProfileId);

    private sealed record CreateFixedExpenseFromTransactionRequestBody(
        string? Name, DateOnly? AnchorDate, string? FrequencyUnit, int IntervalMonths, int IntervalWeeks, int DayOfWeek);
}
