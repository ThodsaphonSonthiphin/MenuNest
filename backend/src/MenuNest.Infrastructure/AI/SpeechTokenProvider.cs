using MenuNest.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace MenuNest.Infrastructure.AI;

/// <summary>
/// Exchanges the Azure Speech subscription key for a short-lived access
/// token by calling the Cognitive Services STS endpoint.
/// </summary>
public sealed class SpeechTokenProvider : ISpeechTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureSpeechOptions _options;

    public SpeechTokenProvider(IHttpClientFactory httpClientFactory, IOptions<AzureSpeechOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<(string Token, string Region)> GetTokenAsync(CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient();
        var url = $"https://{_options.Region}.api.cognitive.microsoft.com/sts/v1.0/issueToken";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.SubscriptionKey);
        request.Content = new StringContent(string.Empty);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadAsStringAsync(ct);
        return (token, _options.Region);
    }
}
