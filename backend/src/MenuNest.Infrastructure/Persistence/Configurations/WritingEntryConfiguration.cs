using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class WritingEntryConfiguration : IEntityTypeConfiguration<WritingEntry>
{
    public void Configure(EntityTypeBuilder<WritingEntry> builder)
    {
        builder.ToTable("WritingEntries");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.UserId).IsRequired();
        builder.Property(w => w.Date).IsRequired();
        builder.Property(w => w.Text).IsRequired();
        builder.Property(w => w.ElapsedSeconds).IsRequired();
        builder.Property(w => w.WordsPerMinute).IsRequired();

        // Phase 2 (record_writing_correction) -- nullable, unpopulated in Phase 1.
        builder.Property(w => w.TargetRule).HasMaxLength(200);
        builder.Property(w => w.ThaiWhyLine).HasMaxLength(2000);

        // Hot query for Phase 2's list_pending_writing_entries (CorrectedAt IS NULL)
        // and for a future "my entries" list -- both filter/sort by user + date.
        builder.HasIndex(w => new { w.UserId, w.Date });

        // Same NoAction rationale as Trip/Intake's User FK (see TripConfiguration,
        // IntakeConfiguration): avoids SQL Server's multi-cascade-path rejection
        // across the User's other relationships.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
