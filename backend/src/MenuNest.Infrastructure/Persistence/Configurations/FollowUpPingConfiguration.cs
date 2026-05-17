using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class FollowUpPingConfiguration : IEntityTypeConfiguration<FollowUpPing>
{
    public void Configure(EntityTypeBuilder<FollowUpPing> builder)
    {
        builder.ToTable("FollowUpPings");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.SymptomEpisodeId).IsRequired();
        builder.Property(p => p.ScheduledAt).IsRequired();
        builder.Property(p => p.AskedAt);
        builder.Property(p => p.RespondedAt);
        builder.Property(p => p.Response);
        builder.Property(p => p.SeverityAtCheck);
        builder.Property(p => p.Status).IsRequired();

        // Dispatcher query: WHERE Status = Pending AND ScheduledAt <= @now
        // — covering index on (Status, ScheduledAt) makes this O(log n).
        builder.HasIndex(p => new { p.Status, p.ScheduledAt });

        // Episode → pings lookup (cancel pending when episode resolves).
        builder.HasIndex(p => p.SymptomEpisodeId);

        builder.HasOne<SymptomEpisode>()
            .WithMany()
            .HasForeignKey(p => p.SymptomEpisodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
