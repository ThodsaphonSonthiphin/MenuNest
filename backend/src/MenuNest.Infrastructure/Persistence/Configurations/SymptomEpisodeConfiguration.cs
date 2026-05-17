using System.Text.Json;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class SymptomEpisodeConfiguration : IEntityTypeConfiguration<SymptomEpisode>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<SymptomEpisode> builder)
    {
        builder.ToTable("SymptomEpisodes");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.UserId).IsRequired();
        builder.Property(e => e.SymptomId).IsRequired();
        builder.Property(e => e.StartedAt).IsRequired();
        builder.Property(e => e.EndedAt);
        builder.Property(e => e.Severity).IsRequired();
        builder.Property(e => e.SeverityAfter);
        builder.Property(e => e.IsOnPeriod).IsRequired();
        builder.Property(e => e.NoDrugTaken).IsRequired();
        builder.Property(e => e.NoDrugReasonCode);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.RetroClosed).IsRequired();
        builder.Property(e => e.RetroEstimatedDuration).HasMaxLength(50);

        // Migraine-specific nullable columns
        builder.Property(e => e.HasAura);
        builder.Property(e => e.AuraDurationMin);
        builder.Property(e => e.Location);
        builder.Property(e => e.Quality);
        builder.Property(e => e.WorsenedByActivity);
        builder.Property(e => e.FunctionalImpact);

        // JSON columns for multi-value attributes
        var guidListComparer = new ValueComparer<IReadOnlyList<Guid>>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
            v => v.ToList());

        builder.Property(e => e.TriggerIds)
            .HasColumnName("TriggerIdsJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<Guid>>(v, JsonOptions) ?? new List<Guid>(),
                guidListComparer);

        var auraComparer = new ValueComparer<IReadOnlyList<AuraType>>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, (int)x)),
            v => v.ToList());

        builder.Property(e => e.AuraTypes)
            .HasColumnName("AuraTypesJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<AuraType>>(v, JsonOptions) ?? new List<AuraType>(),
                auraComparer);

        var assocComparer = new ValueComparer<IReadOnlyList<AssociatedSymptom>>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, (int)x)),
            v => v.ToList());

        builder.Property(e => e.AssociatedSymptoms)
            .HasColumnName("AssociatedSymptomsJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<AssociatedSymptom>>(v, JsonOptions) ?? new List<AssociatedSymptom>(),
                assocComparer);

        // Hot query: active episodes by user + history by date.
        builder.HasIndex(e => new { e.UserId, e.StartedAt });
        builder.HasIndex(e => new { e.UserId, e.EndedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Symptom>()
            .WithMany()
            .HasForeignKey(e => e.SymptomId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
