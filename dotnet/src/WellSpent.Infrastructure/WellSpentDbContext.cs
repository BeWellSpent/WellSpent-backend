using Microsoft.EntityFrameworkCore;
using WellSpent.Domain.Entities;
using WellSpent.Infrastructure.Persistence;

namespace WellSpent.Infrastructure;

/// <summary>
/// EF Core context for the existing WellSpent Postgres schema. Hand-mapped via
/// Fluent API against tables created by the Go backend's goose migrations — the
/// 59 existing SQL files stay the schema source of truth, not EF Core
/// migrations. Entity configurations are added as each domain needs to query
/// a table — see Persistence/*EntityConfiguration.cs.
/// </summary>
public class WellSpentDbContext(DbContextOptions<WellSpentDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OAuthAccount> OAuthAccounts => Set<OAuthAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserEntityConfiguration());
        modelBuilder.ApplyConfiguration(new OAuthAccountEntityConfiguration());
    }
}
