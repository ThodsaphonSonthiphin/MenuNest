using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class IntakeConfiguration : IEntityTypeConfiguration<Intake>
{
    public void Configure(EntityTypeBuilder<Intake> builder)
    {
        builder.ToTable("Intakes");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.UserId).IsRequired();
        builder.Property(i => i.DrugId).IsRequired();
        builder.Property(i => i.SymptomEpisodeId);
        builder.Property(i => i.TakenAt).IsRequired();
        builder.Property(i => i.DoseAmount).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(1000);

        // Hot queries: recent intakes by user (active-drug calc),
        // intakes by episode (treatment-efficacy stats).
        builder.HasIndex(i => new { i.UserId, i.TakenAt });
        builder.HasIndex(i => i.SymptomEpisodeId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Drug>()
            .WithMany()
            .HasForeignKey(i => i.DrugId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SymptomEpisode>()
            .WithMany()
            .HasForeignKey(i => i.SymptomEpisodeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
