using System.Text.Json;
using MenuNest.Domain.Entities;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class PlaceProfileConfiguration : IEntityTypeConfiguration<PlaceProfile>
{
    public void Configure(EntityTypeBuilder<PlaceProfile> b)
    {
        b.ToTable("PlaceProfiles");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.UserId).IsRequired();
        b.Property(p => p.GooglePlaceId).IsRequired().HasMaxLength(400);

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

        // One profile per Google place per user (mirrors TripPlace's (TripId, GooglePlaceId)).
        b.HasIndex(p => new { p.UserId, p.GooglePlaceId }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
