using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.ValueObjects;

/// <summary>
/// A per-Place link to an external short-video review (framed around TikTok, any http(s) URL).
/// Positional record with a public ctor so System.Text.Json can round-trip it from the JSON
/// column; user input is validated through <see cref="Create"/>.
/// </summary>
public sealed record ReviewLink(string Url, string? Label)
{
    public static ReviewLink Create(string? url, string? label)
    {
        var u = (url ?? string.Empty).Trim();
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new DomainException("Review link must be a valid http(s) URL.");
        if (u.Length > 500) throw new DomainException("Review link URL is too long (max 500).");
        var l = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        if (l is { Length: > 80 }) throw new DomainException("Review link label is too long (max 80).");
        return new ReviewLink(u, l);
    }
}
