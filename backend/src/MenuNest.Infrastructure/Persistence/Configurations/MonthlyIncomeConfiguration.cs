using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class MonthlyIncomeConfiguration : IEntityTypeConfiguration<MonthlyIncome>
{
    public void Configure(EntityTypeBuilder<MonthlyIncome> b)
    {
        b.ToTable("MonthlyIncomes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.Amount).HasColumnType("decimal(18,4)");
        b.HasIndex(x => new { x.FamilyId, x.Year, x.Month }).IsUnique();
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
    }
}
