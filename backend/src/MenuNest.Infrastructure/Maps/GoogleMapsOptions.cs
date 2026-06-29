namespace MenuNest.Infrastructure.Maps;

public sealed class GoogleMapsOptions
{
    public const string SectionName = "GoogleMaps";
    public string? ApiKey { get; set; }      // server-side: Places + Routes + Geocoding
    public string? BrowserKey { get; set; }   // Maps JS (referrer-restricted) — surfaced to SPA build
    public string? MapId { get; set; }        // "DEMO_MAP_ID" in dev
}
