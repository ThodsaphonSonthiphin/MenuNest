using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("Photos");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.ParentType).IsRequired();
        builder.Property(p => p.ParentId).IsRequired();
        builder.Property(p => p.BlobUrl).IsRequired().HasMaxLength(500);
        builder.Property(p => p.ContainerName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.FileSize).IsRequired();
        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.DeletedAt);

        // Primary lookup: "all photos of this entity".
        builder.HasIndex(p => new { p.ParentType, p.ParentId, p.DeletedAt });

        // Audit: "all photos this user has uploaded".
        builder.HasIndex(p => new { p.UserId, p.CreatedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
