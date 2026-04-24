using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class BudgetCategoryGroupConfiguration : IEntityTypeConfiguration<BudgetCategoryGroup>
{
    public void Configure(EntityTypeBuilder<BudgetCategoryGroup> b)
    {
        b.ToTable("BudgetCategoryGroups");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.HasIndex(x => new { x.FamilyId, x.SortOrder });
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
    }
}
