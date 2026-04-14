using Mediator;
using MenuNest.Application.UseCases.Chat;
using MenuNest.Application.UseCases.Chat.CreateConversation;
using MenuNest.Application.UseCases.Chat.DeleteConversation;
using MenuNest.Application.UseCases.Chat.GetMessages;
using MenuNest.Application.UseCases.Chat.GetSpeechToken;
using MenuNest.Application.UseCases.Chat.ListConversations;
using MenuNest.Application.UseCases.Chat.SendMessage;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChatController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyList<ConversationSummaryDto>>> ListConversations(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListConversationsQuery(), ct);
        return Ok(result);
    }

    [HttpPost("conversations")]
    public async Task<ActionResult<ConversationSummaryDto>> CreateConversation(CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateConversationCommand(), ct);
        return Ok(result);
    }

    [HttpGet("conversations/{id:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetMessages(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMessagesQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("conversations/{id:guid}/messages")]
    public async Task<ActionResult<SendMessageResponseDto>> SendMessage(
        Guid id,
        [FromBody] SendMessageRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new SendMessageCommand(id, request.Content), ct);
        return Ok(result);
    }

    [HttpDelete("conversations/{id:guid}")]
    public async Task<IActionResult> DeleteConversation(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteConversationCommand(id), ct);
        return NoContent();
    }

    [HttpGet("speech-token")]
    public async Task<ActionResult<SpeechTokenDto>> GetSpeechToken(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSpeechTokenQuery(), ct);
        return Ok(result);
    }
}

public sealed record SendMessageRequest(string Content);
