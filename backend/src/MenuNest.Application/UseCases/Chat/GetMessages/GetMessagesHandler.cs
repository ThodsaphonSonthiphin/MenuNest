using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Chat.GetMessages;

public sealed class GetMessagesHandler : IQueryHandler<GetMessagesQuery, IReadOnlyList<ChatMessageDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetMessagesHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<ChatMessageDto>> Handle(GetMessagesQuery query, CancellationToken ct)
    {
        var (user, _) = await _userProvisioner.RequireFamilyAsync(ct);

        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == query.ConversationId && c.UserId == user.Id, ct)
            ?? throw new DomainException("Conversation not found.");

        return await _db.ChatMessages
            .Where(m => m.ConversationId == conversation.Id && m.Role != Domain.Enums.ChatRole.Tool)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageDto(m.Id, m.Role.ToString(), m.Content, m.StructuredData, m.CreatedAt))
            .ToListAsync(ct);
    }
}
