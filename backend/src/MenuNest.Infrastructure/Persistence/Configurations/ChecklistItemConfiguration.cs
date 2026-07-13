using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> b)
    {
        b.ToTable("ChecklistItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        // One library row per name per user. SQL Server's default collation is
        // case-insensitive, so this also blocks "Umbrella"/"umbrella" dupes in prod;
        // the attach handler additionally reuses by LOWER(name) for provider-independence.
        b.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}