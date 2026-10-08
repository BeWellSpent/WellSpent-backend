using Microsoft.EntityFrameworkCore;

namespace WellSpent.Infrastructure;

/// <summary>
/// EF Core context for the existing WellSpent Postgres schema. Hand-mapped via
/// Fluent API against tables created by the Go backend's goose migrations — the
/// 59 existing SQL files stay the schema source of truth, not EF Core
/// migrations. No entity is mapped here yet; this scaffold only needs the
/// context to prove connectivity (see /health/db). Domain sub-issues add
/// entity configurations as each one needs to query a table.
/// </summary>
public class WellSpentDbContext(DbContextOptions<WellSpentDbContext> options) : DbContext(options)
{
}
