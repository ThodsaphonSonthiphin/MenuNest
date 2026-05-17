using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class ShareLinkConfiguration : IEntityTypeConfiguration<ShareLink>
{
    public void Configure(EntityTypeBuilder<ShareLink> builder)
    {
        builder.ToTable("ShareLinks");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.UserId).IsRequired();

        // SHA-256 hash hex = 64 chars; pad up to 128 for safety.
        builder.Property(l => l.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(l => l.DateFrom).IsRequired();
        builder.Property(l => l.DateTo).IsRequired();
        builder.Property(l => l.ExpiresAt).IsRequired();
        builder.Property(l => l.RevokedAt);
        builder.Property(l => l.LastAccessedAt);
        builder.Property(l => l.AccessCount).IsRequired();

        // Public report endpoint resolves token → ShareLink via this unique index.
        builder.HasIndex(l => l.TokenHash).IsUnique();

        // List "my share links" page sorts by created date for current user.
        builder.HasIndex(l => new { l.UserId, l.CreatedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
