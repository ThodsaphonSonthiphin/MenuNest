using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class StopChecklistEntryConfiguration : IEntityTypeConfiguration<StopChecklistEntry>
{
    public void Configure(EntityTypeBuilder<StopChecklistEntry> b)
    {
        b.ToTable("StopChecklistEntries");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.StopId).IsRequired();
        b.Property(e => e.ChecklistItemId).IsRequired();
        b.Property(e => e.IsChecked).IsRequired().HasDefaultValue(false);
        // An item attaches to a stop at most once.
        b.HasIndex(e => new { e.StopId, e.ChecklistItemId }).IsUnique();
        // Deleting a Stop (or a Trip cascading its Stops) removes its entries…
        b.HasOne<Stop>().WithMany().HasForeignKey(e => e.StopId).OnDelete(DeleteBehavior.Cascade);
        // …but deleting a Stop must NEVER touch the library item (ADR-059).
        b.HasOne<ChecklistItem>().WithMany().HasForeignKey(e => e.ChecklistItemId).OnDelete(DeleteBehavior.Restrict);
    }
}