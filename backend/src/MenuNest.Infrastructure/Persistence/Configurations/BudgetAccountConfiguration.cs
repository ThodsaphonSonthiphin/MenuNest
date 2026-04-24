using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class BudgetAccountConfiguration : IEntityTypeConfiguration<BudgetAccount>
{
    public void Configure(EntityTypeBuilder<BudgetAccount> b)
    {
        b.ToTable("BudgetAccounts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Balance).HasColumnType("decimal(18,4)");

        // Optimistic-concurrency token: shadow rowversion column protects
        // Balance against lost updates when two transactions mutate the
        // same account concurrently.
        b.Property<byte[]>("RowVersion").IsRowVersion();

        b.HasIndex(x => new { x.FamilyId, x.SortOrder });
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
    }
}
