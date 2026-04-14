using Mediator;
using MenuNest.Application.Abstractions;

namespace MenuNest.Application.UseCases.Chat.GetSpeechToken;

public sealed class GetSpeechTokenHandler : IQueryHandler<GetSpeechTokenQuery, SpeechTokenDto>
{
    private readonly ISpeechTokenProvider _speechTokenProvider;

    public GetSpeechTokenHandler(ISpeechTokenProvider speechTokenProvider)
    {
        _speechTokenProvider = speechTokenProvider;
    }

    public async ValueTask<SpeechTokenDto> Handle(GetSpeechTokenQuery query, CancellationToken ct)
    {
        var (token, region) = await _speechTokenProvider.GetTokenAsync(ct);
        return new SpeechTokenDto(token, region);
    }
}
