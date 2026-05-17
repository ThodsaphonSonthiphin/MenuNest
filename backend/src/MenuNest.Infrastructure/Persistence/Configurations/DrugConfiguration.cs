using System.Text.Json;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class DrugConfiguration : IEntityTypeConfiguration<Drug>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<Drug> builder)
    {
        builder.ToTable("Drugs");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.UserId).IsRequired();
        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.ActiveIngredient).HasMaxLength(200);
        builder.Property(d => d.DrugType).IsRequired();
        builder.Property(d => d.DoseStrength).IsRequired().HasMaxLength(50);
        builder.Property(d => d.EffectDurationMinHours).IsRequired();
        builder.Property(d => d.EffectDurationMaxHours).IsRequired();
        builder.Property(d => d.MaxDailyDose).IsRequired();
        builder.Property(d => d.StockCount).IsRequired();
        builder.Property(d => d.ExpirationDate);
        builder.Property(d => d.UsageNote).HasMaxLength(1000);
        builder.Property(d => d.DeletedAt);

        // M2M "treats" stored as JSON list of Symptom IDs (denormalized for
        // single-row drug fetches; reverse lookup uses JSON contains).
        var guidListComparer = new ValueComparer<IReadOnlyList<Guid>>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
            v => v.ToList());

        builder.Property(d => d.TreatsSymptomIds)
            .HasColumnName("TreatsSymptomIdsJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<List<Guid>>(v, JsonOptions) ?? new List<Guid>(),
                guidListComparer);

        builder.HasIndex(d => new { d.UserId, d.DeletedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
