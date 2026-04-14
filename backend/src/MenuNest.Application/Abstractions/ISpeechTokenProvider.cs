namespace MenuNest.Application.Abstractions;

/// <summary>
/// Returns a short-lived Azure Speech Service token together with the
/// configured region string. The token is obtained from the STS endpoint
/// in Infrastructure; callers never need to reference Azure SDK types.
/// </summary>
public interface ISpeechTokenProvider
{
    Task<(string Token, string Region)> GetTokenAsync(CancellationToken ct = default);
}
