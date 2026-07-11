using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class ItineraryDayConfiguration : IEntityTypeConfiguration<ItineraryDay>
{
    public void Configure(EntityTypeBuilder<ItineraryDay> b)
    {
        b.ToTable("ItineraryDays");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.TripId).IsRequired();
        b.Property(d => d.Date).IsRequired();
        b.Property(d => d.DayStartTime).IsRequired();
        b.Property(d => d.UseCurrentTimeAsStart).IsRequired().HasDefaultValue(false);
        b.HasIndex(d => new { d.TripId, d.Date }).IsUnique();
        b.HasOne<Trip>().WithMany().HasForeignKey(d => d.TripId).OnDelete(DeleteBehavior.Cascade);
    }
}
