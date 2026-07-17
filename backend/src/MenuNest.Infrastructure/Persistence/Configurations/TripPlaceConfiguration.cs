using System.Text.Json;
using MenuNest.Domain.Entities;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var reviewConverter = new ValueConverter<IReadOnlyList<ReviewLink>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => string.IsNullOrEmpty(v)
                ? new List<ReviewLink>()
                : JsonSerializer.Deserialize<List<ReviewLink>>(v, jsonOpts) ?? new List<ReviewLink>());
        var reviewComparer = new ValueComparer<IReadOnlyList<ReviewLink>>(
            (a, b) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(b, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<ReviewLink>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        b.Property(p => p.ReviewLinks)
            .HasConversion(reviewConverter, reviewComparer)
            .HasColumnName("ReviewLinksJson")
            .HasColumnType("nvarchar(max)")
            .HasField("_reviewLinks")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasDefaultValueSql("'[]'");
        var seasonConverter = new ValueConverter<IReadOnlyList<SeasonPeriod>, string>(
            v => JsonSerializer.Serialize(v, jsonOpts),
            v => string.IsNullOrEmpty(v)
                ? new List<SeasonPeriod>()
                : JsonSerializer.Deserialize<List<SeasonPeriod>>(v, jsonOpts) ?? new List<SeasonPeriod>());
        var seasonComparer = new ValueComparer<IReadOnlyList<SeasonPeriod>>(
            (a, b) => JsonSerializer.Serialize(a, jsonOpts) == JsonSerializer.Serialize(b, jsonOpts),
            v => JsonSerializer.Serialize(v, jsonOpts).GetHashCode(),
            v => JsonSerializer.Deserialize<List<SeasonPeriod>>(JsonSerializer.Serialize(v, jsonOpts), jsonOpts)!);
        b.Property(p => p.SeasonPeriods)
            .HasConversion(seasonConverter, seasonComparer)
            .HasColumnName("SeasonPeriodsJson")
            .HasColumnType("nvarchar(max)")
            .HasField("_seasonPeriods")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasDefaultValueSql("'[]'");
        b.HasIndex(p => p.TripId);
        // dedupe re-pastes of the same Google place within a trip (filtered: only non-null)
        b.HasIndex(p => new { p.TripId, p.GooglePlaceId })
            .IsUnique()
            .HasFilter("[GooglePlaceId] IS NOT NULL");
        b.HasOne<Trip>().WithMany().HasForeignKey(p => p.TripId).OnDelete(DeleteBehavior.Cascade);
    }
}
