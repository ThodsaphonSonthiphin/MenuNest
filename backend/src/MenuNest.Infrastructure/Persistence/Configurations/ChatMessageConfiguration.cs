using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.ConversationId).IsRequired();
        builder.Property(m => m.Role).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.ToolCalls).HasColumnType("nvarchar(max)");
        builder.Property(m => m.ToolName).HasMaxLength(100);
        builder.Property(m => m.StructuredData).HasColumnType("nvarchar(max)");
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}
