using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class PlaceChecklistEntryConfiguration : IEntityTypeConfiguration<PlaceChecklistEntry>
{
    public void Configure(EntityTypeBuilder<PlaceChecklistEntry> b)
    {
        b.ToTable("PlaceChecklistEntries");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.TripPlaceId).IsRequired();
        b.Property(e => e.ChecklistItemId).IsRequired();
        b.Property(e => e.IsChecked).IsRequired();
        // An item attaches to a place at most once.
        b.HasIndex(e => new { e.TripPlaceId, e.ChecklistItemId }).IsUnique();
        // Deleting a Place (or a Trip cascading its Places) removes its entries…
        b.HasOne<TripPlace>().WithMany().HasForeignKey(e => e.TripPlaceId).OnDelete(DeleteBehavior.Cascade);
        // …but deleting a Place must NEVER touch the library item (ADR-059).
        b.HasOne<ChecklistItem>().WithMany().HasForeignKey(e => e.ChecklistItemId).OnDelete(DeleteBehavior.Restrict);
    }
}