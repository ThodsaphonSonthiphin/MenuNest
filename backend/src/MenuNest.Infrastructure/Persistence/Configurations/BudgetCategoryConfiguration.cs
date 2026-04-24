using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class BudgetCategoryConfiguration : IEntityTypeConfiguration<BudgetCategory>
{
    public void Configure(EntityTypeBuilder<BudgetCategory> b)
    {
        b.ToTable("BudgetCategories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.GroupId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.Property(x => x.Emoji).HasMaxLength(8);
        b.Property(x => x.TargetType).HasConversion<int>();
        b.Property(x => x.TargetAmount).HasColumnType("decimal(18,4)");
        b.HasIndex(x => new { x.FamilyId, x.GroupId, x.SortOrder });
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BudgetCategoryGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
    }
}
