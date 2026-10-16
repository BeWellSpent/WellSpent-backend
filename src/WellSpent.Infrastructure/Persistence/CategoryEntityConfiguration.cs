using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WellSpent.Domain.Entities;

namespace WellSpent.Infrastructure.Persistence;

public sealed class CategoryEntityConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("category");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.TypeId).HasColumnName("type_id");
        b.Property(x => x.IsSystem).HasColumnName("is_system");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Color).HasColumnName("color");
        b.Property(x => x.SystemKey).HasColumnName("system_key");
        b.Property(x => x.IsActive).HasColumnName("is_active");
    }
}
