using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class BudgetTransactionConfiguration : IEntityTypeConfiguration<BudgetTransaction>
{
    public void Configure(EntityTypeBuilder<BudgetTransaction> b)
    {
        b.ToTable("BudgetTransactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.AccountId).IsRequired();
        b.Property(x => x.Amount).HasColumnType("decimal(18,4)");
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => new { x.FamilyId, x.Date });
        b.HasIndex(x => new { x.FamilyId, x.CategoryId, x.Date });
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BudgetAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BudgetCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
