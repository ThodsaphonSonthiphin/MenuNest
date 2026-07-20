using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    public void Configure(EntityTypeBuilder<OAuthClient> builder)
    {
        builder.ToTable("OAuthClients");
        builder.HasKey(c => c.ClientId);
        builder.Property(c => c.ClientId).ValueGeneratedNever().HasMaxLength(64);
        builder.Property(c => c.ClientName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.RedirectUrisJson).IsRequired();
        builder.Property(c => c.Scope);
        builder.Property(c => c.ExpiresAt).IsRequired();
    }
}
