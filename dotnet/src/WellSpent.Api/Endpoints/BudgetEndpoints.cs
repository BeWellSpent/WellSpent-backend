using MediatR;
using WellSpent.Api.Auth;
using WellSpent.Application.Budgets.AddBudgetPeople;
using WellSpent.Application.Budgets.AddIncomeSource;
using WellSpent.Application.Budgets.AddSavingsSource;
using WellSpent.Application.Budgets.CreateBudgetPeriod;
using WellSpent.Application.Budgets.CreateBudgetProfile;
using WellSpent.Application.Budgets.DeleteBudgetProfile;
using WellSpent.Application.Budgets.DeleteIncomeSource;
using WellSpent.Application.Budgets.DeleteSavingsSource;
using WellSpent.Application.Budgets.GetBudgetPeriod;
using WellSpent.Application.Budgets.GetBudgetProfile;
using WellSpent.Application.Budgets.ListBudgetPeople;
using WellSpent.Application.Budgets.ListBudgetPeriods;
using WellSpent.Application.Budgets.ListBudgetProfiles;
using WellSpent.Application.Budgets.ListIncomeEntries;
using WellSpent.Application.Budgets.ListIncomeSources;
using WellSpent.Application.Budgets.ListSavingsSources;
using WellSpent.Application.Budgets.RemoveBudgetPerson;
using WellSpent.Application.Budgets.SetBudgetAutoUpdatePlannedAmount;
using WellSpent.Application.Budgets.SetBudgetCarryoverEnabled;
using WellSpent.Application.Budgets.UpdateBudgetPerson;
using WellSpent.Application.Budgets.UpdateBudgetPersonRole;
using WellSpent.Application.Budgets.UpdateBudgetProfile;
using WellSpent.Application.Budgets.UpdateIncomeEntry;
using WellSpent.Application.Budgets.UpdateIncomeSource;
using WellSpent.Application.Budgets.UpdateMyBudgetPreferences;
using WellSpent.Application.Budgets.UpdateMyFocusedViewPreference;
using WellSpent.Application.Budgets.UpdateMyManualMatchReviewPreference;
using WellSpent.Application.Budgets.UpdateSavingsSource;
using WellSpent.Application.Common;
using WellSpent.Application.ExpenseAllocations;
using WellSpent.Application.ExpenseAllocations.DeleteExpenseAllocation;
using WellSpent.Application.ExpenseAllocations.ListExpenseAllocations;
using WellSpent.Application.ExpenseAllocations.UpsertExpenseAllocation;
using WellSpent.Application.ExpenseSummary.GetExpenseSummary;
using WellSpent.Application.FixedExpenses;
using WellSpent.Application.FixedExpenses.CreateFixedExpense;
using WellSpent.Application.FixedExpenses.DeleteFixedExpense;
using WellSpent.Application.FixedExpenses.ListFixedExpenses;
using WellSpent.Application.FixedExpenses.UpdateFixedExpense;
using WellSpent.Application.PaymentMethods.ListPaymentMethods;

namespace WellSpent.Api.Endpoints;

public static class BudgetEndpoints
{
    public static void MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/rest/v1/budgets").WithTags("budgets").RequireAuthorization();

        // ── Profile CRUD ─────────────────────────────────────────────────
        group.MapPost("", async (CreateBudgetProfileRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateBudgetProfileCommand(CurrentUser.GetId(ctx), body.Name, body.Cycle), ct);
            return Results.Ok(new { profile = result.Profile, period = result.Period });
        });

        group.MapGet("", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profiles = await sender.Send(new ListBudgetProfilesQuery(CurrentUser.GetId(ctx)), ct);
            return Results.Ok(new { profiles });
        });

        group.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profile = await sender.Send(new GetBudgetProfileQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { profile });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateBudgetProfileRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profile = await sender.Send(new UpdateBudgetProfileCommand(CurrentUser.GetId(ctx), id, body.Name, body.Cycle), ct);
            return Results.Ok(new { profile });
        });

        group.MapPut("/{id:guid}/carryover-enabled", async (Guid id, EnabledRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profile = await sender.Send(new SetBudgetCarryoverEnabledCommand(CurrentUser.GetId(ctx), id, body.Enabled), ct);
            return Results.Ok(new { profile });
        });

        group.MapPut("/{id:guid}/auto-update-planned-amount", async (Guid id, EnabledRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var profile = await sender.Send(new SetBudgetAutoUpdatePlannedAmountCommand(CurrentUser.GetId(ctx), id, body.Enabled), ct);
            return Results.Ok(new { profile });
        });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteBudgetProfileCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok();
        });

        // ── Period ───────────────────────────────────────────────────────
        group.MapPost("/{id:guid}/periods", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var period = await sender.Send(new CreateBudgetPeriodCommand(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { period });
        });

        group.MapGet("/{id:guid}/periods", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var periods = await sender.Send(new ListBudgetPeriodsQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { periods });
        });

        // ── People ───────────────────────────────────────────────────────
        group.MapPost("/{id:guid}/people", async (Guid id, AddBudgetPeopleRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var people = body.People.Select(p => new NewBudgetPersonInput(p.UserName, p.UserId, p.Color ?? "")).ToList();
            var result = await sender.Send(new AddBudgetPeopleCommand(CurrentUser.GetId(ctx), id, people), ct);
            return Results.Ok(new { people = result });
        });

        group.MapGet("/{id:guid}/people", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var people = await sender.Send(new ListBudgetPeopleQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { people });
        });

        group.MapPut("/{id:guid}/people/{personId:int}", async (Guid id, int personId, UpdatePersonRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateBudgetPersonCommand(CurrentUser.GetId(ctx), id, personId, body.Color), ct);
            return Results.Ok(new { person });
        });

        group.MapPut("/{id:guid}/people/{personId:int}/role", async (Guid id, int personId, UpdateRoleRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateBudgetPersonRoleCommand(CurrentUser.GetId(ctx), id, personId, body.Role), ct);
            return Results.Ok(new { person });
        });

        group.MapDelete("/{id:guid}/people/{personId:int}", async (
            Guid id, int personId, int? replacementPersonId, Guid? replacementPaymentMethodId,
            HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RemoveBudgetPersonCommand(
                CurrentUser.GetId(ctx), id, personId, replacementPersonId ?? 0, replacementPaymentMethodId), ct);
            return Results.Ok();
        });

        group.MapPut("/{id:guid}/my-preferences", async (Guid id, MyPreferencesRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateMyBudgetPreferencesCommand(
                CurrentUser.GetId(ctx), id, body.PlanChartType, body.OverviewChartType), ct);
            return Results.Ok(new { person });
        });

        group.MapPut("/{id:guid}/my-manual-match-review-preference", async (Guid id, EnabledRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateMyManualMatchReviewPreferenceCommand(CurrentUser.GetId(ctx), id, body.Enabled), ct);
            return Results.Ok(new { person });
        });

        group.MapPut("/{id:guid}/my-focused-view-preference", async (Guid id, EnabledRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var person = await sender.Send(new UpdateMyFocusedViewPreferenceCommand(CurrentUser.GetId(ctx), id, body.Enabled), ct);
            return Results.Ok(new { person });
        });

        // ── Payment methods (list only — create/update/delete are top-level, see PaymentMethodEndpoints) ──
        group.MapGet("/{id:guid}/payment-methods", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var methods = await sender.Send(new ListPaymentMethodsQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { methods });
        });

        // ── Income sources ───────────────────────────────────────────────
        group.MapPost("/{id:guid}/income-sources", async (Guid id, IncomeSourceRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var source = await sender.Send(new AddIncomeSourceCommand(
                CurrentUser.GetId(ctx), id, body.Name, body.IncomeType, body.DefaultAmount,
                body.Recurring, body.BudgetPersonId, body.PaymentFrequency, body.BeforeTax), ct);
            return Results.Ok(new { source });
        });

        group.MapGet("/{id:guid}/income-sources", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var sources = await sender.Send(new ListIncomeSourcesQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { sources });
        });

        group.MapPut("/{id:guid}/income-sources/{sourceId:int}", async (
            Guid id, int sourceId, IncomeSourceRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var source = await sender.Send(new UpdateIncomeSourceCommand(
                CurrentUser.GetId(ctx), sourceId, id, body.Name, body.IncomeType, body.DefaultAmount,
                body.Recurring, body.BudgetPersonId, body.PaymentFrequency, body.BeforeTax), ct);
            return Results.Ok(new { source });
        });

        group.MapDelete("/{id:guid}/income-sources/{sourceId:int}", async (Guid id, int sourceId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteIncomeSourceCommand(CurrentUser.GetId(ctx), sourceId, id), ct);
            return Results.Ok();
        });

        // ── Savings sources ──────────────────────────────────────────────
        group.MapPost("/{id:guid}/savings-sources", async (Guid id, SavingsSourceRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var source = await sender.Send(new AddSavingsSourceCommand(
                CurrentUser.GetId(ctx), id, body.Name, body.Amount, body.PaymentMethodId, body.PaymentDays ?? []), ct);
            return Results.Ok(new { source });
        });

        group.MapGet("/{id:guid}/savings-sources", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var sources = await sender.Send(new ListSavingsSourcesQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { sources });
        });

        group.MapPut("/{id:guid}/savings-sources/{sourceId:int}", async (
            Guid id, int sourceId, SavingsSourceRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var source = await sender.Send(new UpdateSavingsSourceCommand(
                CurrentUser.GetId(ctx), sourceId, id, body.Name, body.Amount, body.PaymentMethodId, body.PaymentDays ?? []), ct);
            return Results.Ok(new { source });
        });

        group.MapDelete("/{id:guid}/savings-sources/{sourceId:int}", async (Guid id, int sourceId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteSavingsSourceCommand(CurrentUser.GetId(ctx), sourceId, id), ct);
            return Results.Ok();
        });

        // ── Fixed expenses ───────────────────────────────────────────────
        group.MapPost("/{id:guid}/fixed-expenses", async (Guid id, FixedExpenseRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateFixedExpenseCommand(CurrentUser.GetId(ctx), id, body.ToFields()), ct);
            return Results.Ok(new { fixedExpense = result.Expense, transaction = result.Transaction });
        });

        group.MapGet("/{id:guid}/fixed-expenses", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var fixedExpenses = await sender.Send(new ListFixedExpensesQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { fixedExpenses });
        });

        group.MapPut("/{id:guid}/fixed-expenses/{feId:guid}", async (
            Guid id, Guid feId, FixedExpenseRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var fixedExpense = await sender.Send(new UpdateFixedExpenseCommand(CurrentUser.GetId(ctx), feId, id, body.ToFields()), ct);
            return Results.Ok(new { fixedExpense });
        });

        group.MapDelete("/{id:guid}/fixed-expenses/{feId:guid}", async (Guid id, Guid feId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteFixedExpenseCommand(CurrentUser.GetId(ctx), feId, id), ct);
            return Results.Ok();
        });

        // ── Expense allocations ──────────────────────────────────────────
        group.MapGet("/{id:guid}/expense-allocations", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var allocations = await sender.Send(new ListExpenseAllocationsQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { allocations });
        });

        group.MapPut("/{id:guid}/expense-allocations", async (Guid id, UpsertExpenseAllocationRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var allocation = await sender.Send(new UpsertExpenseAllocationCommand(
                CurrentUser.GetId(ctx), id, body.CategoryId, body.BudgetPersonId, body.PlannedAmount), ct);
            return Results.Ok(new { allocation });
        });

        group.MapDelete("/{id:guid}/expense-allocations/{allocationId:int}", async (Guid id, int allocationId, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteExpenseAllocationCommand(CurrentUser.GetId(ctx), allocationId, id), ct);
            return Results.Ok();
        });

        // Separate namespace — GET /rest/v1/budgets/{id}/periods is the
        // profile-scoped list; a single period is looked up by its own id.
        var periodGroup = app.MapGroup("/rest/v1/budget-periods").WithTags("budgets").RequireAuthorization();
        periodGroup.MapGet("/{id:guid}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var period = await sender.Send(new GetBudgetPeriodQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { period });
        });

        // ── Income entries ───────────────────────────────────────────────
        periodGroup.MapGet("/{id:guid}/income-entries", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var entries = await sender.Send(new ListIncomeEntriesQuery(CurrentUser.GetId(ctx), id), ct);
            return Results.Ok(new { entries });
        });

        periodGroup.MapPut("/{id:guid}/income-entries/{entryId:int}", async (
            Guid id, int entryId, UpdateIncomeEntryRequestBody body, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var entry = await sender.Send(new UpdateIncomeEntryCommand(CurrentUser.GetId(ctx), entryId, id, body.Amount), ct);
            return Results.Ok(new { entry });
        });

        // ── Expense summary ──────────────────────────────────────────────
        periodGroup.MapGet("/{id:guid}/expense-summary", async (Guid id, bool? focusedView, HttpContext ctx, ISender sender, CancellationToken ct) =>
        {
            var summary = await sender.Send(new GetExpenseSummaryQuery(CurrentUser.GetId(ctx), id, focusedView ?? false), ct);
            return Results.Ok(summary);
        });
    }

    private sealed record CreateBudgetProfileRequestBody(string Name, string Cycle);
    private sealed record UpdateBudgetProfileRequestBody(string Name, string Cycle);
    private sealed record EnabledRequestBody(bool Enabled);
    private sealed record UpdatePersonRequestBody(string Color);
    private sealed record UpdateRoleRequestBody(string Role);
    private sealed record MyPreferencesRequestBody(string? PlanChartType, string? OverviewChartType);
    private sealed record NewPersonRequestBody(string UserName, Guid? UserId, string? Color);
    private sealed record AddBudgetPeopleRequestBody(List<NewPersonRequestBody> People);

    private sealed record IncomeSourceRequestBody(
        string Name, string IncomeType, Money DefaultAmount, bool Recurring,
        int? BudgetPersonId, string PaymentFrequency, bool BeforeTax);

    private sealed record SavingsSourceRequestBody(string Name, Money Amount, Guid? PaymentMethodId, int[]? PaymentDays);

    private sealed record FixedExpenseRequestBody(
        string Name, Money PlannedAmount, int? CategoryId, Guid? PaymentMethodId,
        int DayOfMonth, int IntervalMonths, DateOnly? AnchorDate, string? FrequencyUnit,
        int IntervalWeeks, int DayOfWeek, DateOnly? EndDate, int TotalPayments)
    {
        public FixedExpenseFields ToFields() => new(
            Name, PlannedAmount, CategoryId, PaymentMethodId, DayOfMonth, IntervalMonths,
            AnchorDate, FrequencyUnit, IntervalWeeks, DayOfWeek, EndDate, TotalPayments);
    }

    private sealed record UpdateIncomeEntryRequestBody(Money Amount);

    private sealed record UpsertExpenseAllocationRequestBody(int CategoryId, int? BudgetPersonId, Money PlannedAmount);
}
