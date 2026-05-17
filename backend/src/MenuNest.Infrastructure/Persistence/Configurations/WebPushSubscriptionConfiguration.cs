using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class WebPushSubscriptionConfiguration : IEntityTypeConfiguration<WebPushSubscription>
{
    public void Configure(EntityTypeBuilder<WebPushSubscription> builder)
    {
        builder.ToTable("WebPushSubscriptions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.UserId).IsRequired();

        // Endpoint URLs can be very long (FCM/Mozilla); no max length cap.
        builder.Property(s => s.Endpoint).IsRequired();
        builder.Property(s => s.P256dh).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Auth).IsRequired().HasMaxLength(100);
        builder.Property(s => s.ExpiresAt);
        builder.Property(s => s.LastSuccessAt);
        builder.Property(s => s.LastFailureAt);

        // Lookup by user when dispatcher needs to send to all of a user's
        // registered devices.
        builder.HasIndex(s => s.UserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
