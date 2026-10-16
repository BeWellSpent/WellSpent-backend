namespace WellSpent.Domain.Entities;

/// <summary>User-defined or global system category label. `Id` is the table's SERIAL (int4) PK.</summary>
public sealed class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? TypeId { get; set; }
    public bool IsSystem { get; set; }

    /// <summary>Null for a global system category.</summary>
    public Guid? UserId { get; set; }

    public string Color { get; set; } = "";

    /// <summary>Stable language-independent identity for a seeded system category (e.g. "groceries", "income"). Null for user categories.</summary>
    public string? SystemKey { get; set; }

    public bool IsActive { get; set; } = true;
}
