using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

public sealed class ChatConversation : Entity
{
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string Title { get; private set; } = string.Empty;

    private readonly List<ChatMessage> _messages = new();
    public IReadOnlyCollection<ChatMessage> Messages => _messages.AsReadOnly();

    private ChatConversation() { }

    public static ChatConversation Create(Guid userId, Guid familyId, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Conversation title cannot be empty.");

        return new ChatConversation
        {
            UserId = userId,
            FamilyId = familyId,
            Title = title.Length > 100 ? title[..100] : title
        };
    }

    public void UpdateTitle(string title)
    {
        Title = title.Length > 100 ? title[..100] : title;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
