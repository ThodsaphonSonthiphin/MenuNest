using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class MonthlyAssignmentConfiguration : IEntityTypeConfiguration<MonthlyAssignment>
{
    public void Configure(EntityTypeBuilder<MonthlyAssignment> b)
    {
        b.ToTable("MonthlyAssignments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.CategoryId).IsRequired();
        b.Property(x => x.AssignedAmount).HasColumnType("decimal(18,4)");
        b.HasIndex(x => new { x.FamilyId, x.CategoryId, x.Year, x.Month }).IsUnique();
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BudgetCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
