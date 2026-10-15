using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Categories.ListCategories;

/// <summary>
/// No membership check: categories are user-scoped (system + the caller's
/// own), not budget-scoped. When BudgetProfileId is given, also includes
/// categories referenced by that budget's transactions/fixed expenses, so a
/// collaborator can see categories the owner created — matching Go exactly,
/// including the absence of a membership check on that budget id.
/// </summary>
public sealed record ListCategoriesQuery(Guid UserId, Guid? BudgetProfileId) : IRequest<List<CategoryDto>>;

public sealed class ListCategoriesQueryHandler(ITransactionRepository transactions)
    : IRequestHandler<ListCategoriesQuery, List<CategoryDto>>
{
    public async Task<List<CategoryDto>> Handle(ListCategoriesQuery request, CancellationToken ct)
    {
        var rows = request.BudgetProfileId is { } profileId
            ? await transactions.ListCategoriesForBudgetAsync(request.UserId, profileId, ct)
            : await transactions.ListCategoriesAsync(request.UserId, ct);
        return rows.Select(CategoryMapping.ToDto).ToList();
    }
}
