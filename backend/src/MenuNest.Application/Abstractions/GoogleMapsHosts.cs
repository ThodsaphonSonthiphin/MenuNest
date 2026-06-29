namespace MenuNest.Application.Abstractions;

/// <summary>
/// The single allowlist of Google-owned hosts a shared Maps link may touch.
/// Used by both the resolve-place validator (gates the input URL before any fetch)
/// and the place resolver (re-checks the final URL after redirects) — the two-layer
/// SSRF defence for ADR-007. Keep this the one source of truth so the layers cannot drift.
/// </summary>
public static class GoogleMapsHosts
{
    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
        { "maps.app.goo.gl", "goo.gl", "maps.google.com", "www.google.com", "google.com", "g.co" };

    public static bool IsAllowedHost(string host) =>
        Allowed.Contains(host) || host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && IsAllowedHost(uri.Host);
}
