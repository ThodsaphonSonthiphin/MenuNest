using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace MenuNest.Infrastructure.Maps;

/// <summary>
/// Resolves a shared Google Maps URL to authoritative place data. The short link is
/// unfurled server-side (CORS CF1), then a Places API (New) Text Search returns the
/// place_id + snapshot — scraped coords are never the stored truth (ToS, ADR-007).
/// </summary>
public sealed class GooglePlaceResolver : IPlaceResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _http;
    private readonly GoogleMapsOptions _opts;

    public GooglePlaceResolver(IHttpClientFactory http, IOptions<GoogleMapsOptions> opts)
    { _http = http; _opts = opts.Value; }

    public async Task<ResolvedPlaceDto> ResolveFromUrlAsync(string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new DomainException("Maps is not configured.");

        var client = _http.CreateClient();
        // 1) unfurl short links — works whether the client auto-followed (production, final 200)
        //    or the stub returned a raw 3xx (unit test).  Multi-hop chains are handled for free
        //    because RequestMessage.RequestUri reflects the FINAL URL after all auto-redirects.
        var head = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        string longUrl;
        if ((int)head.StatusCode is >= 300 and < 400 && head.Headers.Location is not null)
            longUrl = head.Headers.Location.IsAbsoluteUri
                ? head.Headers.Location.ToString()
                : new Uri(new Uri(url), head.Headers.Location).ToString();
        else
            longUrl = head.RequestMessage?.RequestUri?.ToString() ?? url;

        // 2) extract a text query from the /place/<name>/ segment of the long URL
        var query = ExtractPlaceQuery(longUrl)
            ?? throw new DomainException("Could not read that Google Maps link. Enter the place manually.");

        // 3) Places API (New) Text Search (field mask per the Places-New sub-skill)
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:searchText");
        req.Headers.Add("X-Goog-Api-Key", _opts.ApiKey);
        req.Headers.Add("X-Goog-FieldMask",
            "places.id,places.displayName,places.location,places.formattedAddress,places.priceLevel,places.regularOpeningHours");
        req.Headers.Add("X-Goog-Maps-Solution-ID", "gmp_git_agentskills_v1");
        req.Content = JsonContent.Create(new { textQuery = query });
        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var place = doc.RootElement.GetProperty("places").EnumerateArray().FirstOrDefault();
        if (place.ValueKind == JsonValueKind.Undefined)
            throw new DomainException("No place found for that link. Enter it manually.");

        var loc = place.GetProperty("location");
        return new ResolvedPlaceDto(
            GooglePlaceId: place.GetProperty("id").GetString(),
            Name: place.GetProperty("displayName").GetProperty("text").GetString() ?? query,
            Lat: loc.GetProperty("latitude").GetDouble(),
            Lng: loc.GetProperty("longitude").GetDouble(),
            Address: place.TryGetProperty("formattedAddress", out var a) ? a.GetString() : null,
            Category: PlaceCategory.Other,
            PriceLevel: MapPriceLevel(place),
            PhotoUrl: null,
            OpeningHoursJson: place.TryGetProperty("regularOpeningHours", out var h) ? h.GetRawText() : null);
    }

    public static string? ExtractPlaceQuery(string longUrl)
    {
        var marker = "/place/";
        var i = longUrl.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var rest = longUrl[(i + marker.Length)..];
        var end = rest.IndexOf('/');
        var seg = end >= 0 ? rest[..end] : rest;
        return Uri.UnescapeDataString(seg.Replace('+', ' ')).Trim();
    }

    private static int? MapPriceLevel(JsonElement place) =>
        place.TryGetProperty("priceLevel", out var p) ? p.GetString() switch
        {
            "PRICE_LEVEL_FREE" => 0,
            "PRICE_LEVEL_INEXPENSIVE" => 1,
            "PRICE_LEVEL_MODERATE" => 2,
            "PRICE_LEVEL_EXPENSIVE" => 3,
            "PRICE_LEVEL_VERY_EXPENSIVE" => 4,
            _ => null,
        } : null;
}
