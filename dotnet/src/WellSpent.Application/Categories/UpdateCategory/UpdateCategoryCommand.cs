using MediatR;
using WellSpent.Domain.Abstractions;

namespace WellSpent.Application.Categories.UpdateCategory;

/// <summary>
/// Mirrors a genuine Go quirk: updating a category owned by someone else
/// returns NotFound (404), not Forbidden (403) — the repository's WHERE
/// clause scopes by (id, userId, not-system) directly, so another user's
/// category simply never matches rather than being explicitly rejected.
/// System categories take a separate path (color only, no ownership check —
/// there is none to make).
/// </summary>
public sealed record UpdateCategoryCommand(Guid UserId, int Id, string Name, string Color) : IRequest<CategoryDto>;

public sealed class UpdateCategoryCommandHandler(ITransactionRepository transactions)
    : IRequestHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(UpdateCategoryCommand request, CancellationToken ct)
    {
        var existing = await transactions.GetCategoryAsync(request.Id, ct);
        var updated = existing.IsSystem
            ? await transactions.UpdateSystemCategoryColorAsync(request.Id, request.Color, ct)
            : await transactions.UpdateCategoryAsync(request.Id, request.UserId, request.Name, request.Color, ct);
        return CategoryMapping.ToDto(updated);
    }
}
