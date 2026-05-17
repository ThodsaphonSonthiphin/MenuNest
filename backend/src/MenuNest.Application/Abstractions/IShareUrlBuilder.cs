namespace MenuNest.Application.Abstractions;

/// <summary>
/// Composes the user-visible share URL ({BaseUrl}/share/{token}) for a
/// freshly issued doctor-report token. Lives in Application so handlers
/// don't have to depend on Infrastructure's <c>ShareOptions</c>.
/// </summary>
public interface IShareUrlBuilder
{
    /// <summary>
    /// Returns the full share URL for the given raw token. If no base
    /// URL is configured the result is a relative path the SPA can
    /// resolve against its own origin.
    /// </summary>
    string BuildShareUrl(string rawToken);
}
