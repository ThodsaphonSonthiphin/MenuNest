using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class TripPlaceConfiguration : IEntityTypeConfiguration<TripPlace>
{
    public void Configure(EntityTypeBuilder<TripPlace> b)
    {
        b.ToTable("TripPlaces");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.TripId).IsRequired();
        b.Property(p => p.Name).IsRequired().HasMaxLength(300);
        b.Property(p => p.GooglePlaceId).HasMaxLength(400);
        b.Property(p => p.Address).HasMaxLength(500);
        b.Property(p => p.Category).HasConversion<int>();
        b.Property(p => p.OpeningHoursJson).HasColumnType("nvarchar(max)");
        b.Property(p => p.FeeNote).HasMaxLength(200);
        b.Property(p => p.Notes).HasMaxLength(2000);
        b.HasIndex(p => p.TripId);
        // dedupe re-pastes of the same Google place within a trip (filtered: only non-null)
        b.HasIndex(p => new { p.TripId, p.GooglePlaceId })
            .IsUnique()
            .HasFilter("[GooglePlaceId] IS NOT NULL");
        b.HasOne<Trip>().WithMany().HasForeignKey(p => p.TripId).OnDelete(DeleteBehavior.Cascade);
    }
}
