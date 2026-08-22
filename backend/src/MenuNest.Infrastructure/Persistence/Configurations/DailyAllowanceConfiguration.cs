using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class DailyAllowanceConfiguration : IEntityTypeConfiguration<DailyAllowance>
{
    public void Configure(EntityTypeBuilder<DailyAllowance> b)
    {
        b.ToTable("DailyAllowances");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FamilyId).IsRequired();
        b.Property(x => x.Amount).HasColumnType("decimal(18,4)");
        b.Property(x => x.FrozenPot).HasColumnType("decimal(18,4)");
        b.Property(x => x.FrozenOn).IsRequired();
        b.Property(x => x.ForYear).IsRequired();
        b.Property(x => x.ForMonth).IsRequired();

        // menunest-185: exactly one frozen figure per family, overwritten at each freeze.
        b.HasIndex(x => x.FamilyId).IsUnique();
        b.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Cascade);
    }
}
