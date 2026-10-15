using WellSpent.Domain.Entities;

namespace WellSpent.Application.Categories;

public sealed record CategoryDto(int Id, string Name, bool IsSystem, string Color, string? SystemKey);

public static class CategoryMapping
{
    public static CategoryDto ToDto(Category c) => new(c.Id, c.Name, c.IsSystem, c.Color, c.SystemKey);
}
