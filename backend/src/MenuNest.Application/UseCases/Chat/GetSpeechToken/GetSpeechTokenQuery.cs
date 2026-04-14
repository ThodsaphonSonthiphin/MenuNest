using Mediator;

namespace MenuNest.Application.UseCases.Chat.GetSpeechToken;

public sealed record GetSpeechTokenQuery : IQuery<SpeechTokenDto>;
