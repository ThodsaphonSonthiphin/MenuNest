using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class AppSessionConfiguration : IEntityTypeConfiguration<AppSession>
{
    public void Configure(EntityTypeBuilder<AppSession> builder)
    {
        builder.ToTable("AppSessions");
        builder.HasKey(s => s.RefreshCode);
        builder.Property(s => s.RefreshCode).ValueGeneratedNever().HasMaxLength(128);
        builder.Property(s => s.Subject).IsRequired().HasMaxLength(128);
        builder.Property(s => s.ExpiresAt).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.HasIndex(s => s.Subject);
    }
}
