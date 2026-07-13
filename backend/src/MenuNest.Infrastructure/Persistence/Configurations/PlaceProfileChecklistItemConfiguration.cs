using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class PlaceProfileChecklistItemConfiguration : IEntityTypeConfiguration<PlaceProfileChecklistItem>
{
    public void Configure(EntityTypeBuilder<PlaceProfileChecklistItem> b)
    {
        b.ToTable("PlaceProfileChecklistItems");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.PlaceProfileId).IsRequired();
        b.Property(e => e.ChecklistItemId).IsRequired();
        b.HasIndex(e => new { e.PlaceProfileId, e.ChecklistItemId }).IsUnique();
        // Deleting a profile removes its junction rows…
        b.HasOne<PlaceProfile>().WithMany().HasForeignKey(e => e.PlaceProfileId).OnDelete(DeleteBehavior.Cascade);
        // …but NEVER the library item (ADR-059 rule reused).
        b.HasOne<ChecklistItem>().WithMany().HasForeignKey(e => e.ChecklistItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
