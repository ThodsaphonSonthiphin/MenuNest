using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class OAuthRefreshTokenConfiguration : IEntityTypeConfiguration<OAuthRefreshToken>
{
    public void Configure(EntityTypeBuilder<OAuthRefreshToken> builder)
    {
        builder.ToTable("OAuthRefreshTokens");
        builder.HasKey(r => r.RefreshCode);
        builder.Property(r => r.RefreshCode).ValueGeneratedNever().HasMaxLength(128);
        builder.Property(r => r.EntraRefreshToken).IsRequired();
        builder.Property(r => r.Subject).IsRequired().HasMaxLength(128);
        builder.Property(r => r.ExpiresAt).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
    }
}
