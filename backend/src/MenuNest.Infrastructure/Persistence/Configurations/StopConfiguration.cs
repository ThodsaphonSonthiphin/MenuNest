using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class StopConfiguration : IEntityTypeConfiguration<Stop>
{
    public void Configure(EntityTypeBuilder<Stop> b)
    {
        b.ToTable("Stops");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.ItineraryDayId).IsRequired();
        b.Property(s => s.TripPlaceId).IsRequired();
        b.Property(s => s.Sequence).IsRequired();
        b.Property(s => s.DwellMinutes).IsRequired();
        b.Property(s => s.TravelModeToReach).HasConversion<int>();
        b.Property(s => s.Notes).HasMaxLength(2000);
        b.HasIndex(s => new { s.ItineraryDayId, s.Sequence });
        b.HasOne<ItineraryDay>().WithMany().HasForeignKey(s => s.ItineraryDayId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TripPlace>().WithMany().HasForeignKey(s => s.TripPlaceId).OnDelete(DeleteBehavior.NoAction);
    }
}
