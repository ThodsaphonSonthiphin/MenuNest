using MenuNest.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace MenuNest.Infrastructure.Services;

/// <summary>
/// Default <see cref="IShareUrlBuilder"/>. Combines the configured
/// <see cref="ShareOptions.BaseUrl"/> with the token to produce a
/// fully-qualified URL; when no base URL is configured returns a
/// relative path so the SPA can resolve it against its own origin.
/// </summary>
public sealed class ShareUrlBuilder : IShareUrlBuilder
{
    private readonly ShareOptions _options;

    public ShareUrlBuilder(IOptions<ShareOptions> options)
    {
        _options = options.Value;
    }

    public string BuildShareUrl(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new ArgumentException("Raw token is required.", nameof(rawToken));

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            return $"/share/{rawToken}";

        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/share/{rawToken}";
    }
}
