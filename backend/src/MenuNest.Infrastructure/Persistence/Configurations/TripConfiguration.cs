using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> b)
    {
        b.ToTable("Trips");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.UserId).IsRequired();
        b.Property(t => t.Name).IsRequired().HasMaxLength(200);
        b.Property(t => t.Destination).HasMaxLength(200);
        b.Property(t => t.StartDate).IsRequired();
        b.Property(t => t.DayCount).IsRequired();
        b.Property(t => t.DefaultTravelMode).HasConversion<int>();
        b.HasIndex(t => new { t.UserId, t.DeletedAt });
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
