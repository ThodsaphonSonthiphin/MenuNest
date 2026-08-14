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
        { "maps.app.goo.gl", "goo.gl", "g.co" };

    // A Google ccTLD host: optional sub-labels, then "google.", then a public-suffix
    // shape we accept — a bare TLD (google.de), or a two-level one (google.co.th,
    // google.com.au). Anchored at both ends so "google.co.th.evil.com" cannot match,
    // and the label before "google" must end with a dot so "evilgoogle.com" cannot
    // either. This is deliberately not a full public-suffix list: the set below is
    // the shape Google actually publishes, and every additional character we admit
    // is SSRF surface (ADR-007's two-layer defence).
    //
    // Accepted residual: `[a-z]{2,3}` admits google.<any 2-3 letter TLD>, so a host
    // like google.zip passes even though this app has no reason to fetch it. The
    // exposure requires an attacker to CONTROL google.<tld>, which Google registers
    // defensively across the TLD space — and the alternative, embedding a real
    // public-suffix list, is a dependency and an update treadmill for a link
    // parser. Revisit only if this ever fetches something other than Maps links.
    private static readonly System.Text.RegularExpressions.Regex GoogleCcTld =
        new(@"^(?:[a-z0-9-]+\.)*google\.(?:[a-z]{2,3}|co\.[a-z]{2}|com\.[a-z]{2})$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool IsAllowedHost(string host) =>
        Allowed.Contains(host) || GoogleCcTld.IsMatch(host);

    public static bool IsAllowedUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && IsAllowedHost(uri.Host);
}
