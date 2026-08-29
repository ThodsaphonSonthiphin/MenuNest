using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class BudgetChangeConfiguration : IEntityTypeConfiguration<BudgetChange>
{
    public void Configure(EntityTypeBuilder<BudgetChange> b)
    {
        b.ToTable("BudgetChanges");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Kind).HasConversion<int>().IsRequired();
        b.Property(x => x.Delta).HasColumnType("decimal(18,4)");

        // The list query filters by family + month and orders newest first
        // (menunest-194's window is min(7 days, since the 1st)).
        b.HasIndex(x => new { x.FamilyId, x.Year, x.Month, x.CreatedAt });

        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);

        // Restrict, NOT Cascade: menunest-197 requires a row whose Envelope was
        // deleted to STAY on the list, greyed and unpressable with its reason.
        // Cascade would delete the history row and the reason with it.
        b.HasOne<BudgetCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
