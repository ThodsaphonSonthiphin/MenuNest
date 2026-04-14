using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UseCases.Chat.CreateConversation;

public sealed class CreateConversationHandler : ICommandHandler<CreateConversationCommand, ConversationSummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public CreateConversationHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ConversationSummaryDto> Handle(CreateConversationCommand command, CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);
        var conversation = ChatConversation.Create(user.Id, familyId, "บทสนทนาใหม่");
        _db.ChatConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);

        return new ConversationSummaryDto(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt);
    }
}
