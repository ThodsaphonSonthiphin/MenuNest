using System.Net;
using System.Net.Http;
using FluentAssertions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Maps;
using Microsoft.Extensions.Options;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class GooglePlaceResolverTests
{
    private sealed class SequencedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder(request));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Fact]
    public async Task Resolves_place_id_and_name_via_places_text_search()
    {
        var handler = new SequencedHandler(req =>
        {
            // 1) redirect unfurl of the short link → long URL with the place name
            if (req.RequestUri!.Host.Contains("maps.app.goo.gl"))
            {
                var r = new HttpResponseMessage(HttpStatusCode.Redirect);
                r.Headers.Location = new Uri("https://www.google.com/maps/place/Wat+Phra+That/@18.8,98.9,17z");
                return r;
            }
            // 2) Places Text Search returns the authoritative place
            var body = """
            {"places":[{"id":"ChIJabc","displayName":{"text":"Wat Phra That"},
              "location":{"latitude":18.8049,"longitude":98.9217},
              "formattedAddress":"Chiang Mai","priceLevel":"PRICE_LEVEL_FREE",
              "regularOpeningHours":{"weekdayDescriptions":["Mon: 6AM-6PM"]}}]}
            """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        var http = new HttpClient(handler) { };
        var factory = new SingleClientFactory(http);
        var opts = Options.Create(new GoogleMapsOptions { ApiKey = "demo" });

        var resolver = new GooglePlaceResolver(factory, opts);
        var dto = await resolver.ResolveFromUrlAsync("https://maps.app.goo.gl/abc", CancellationToken.None);

        dto.GooglePlaceId.Should().Be("ChIJabc");
        dto.Name.Should().Be("Wat Phra That");
        dto.Lat.Should().BeApproximately(18.8049, 0.0001);
        dto.Category.Should().Be(PlaceCategory.Other); // category is user-chosen later
    }

    [Theory]
    [InlineData("https://www.google.com/maps/place/Wat+Phra+That/@18.8,98.9,17z", "Wat Phra That")]
    [InlineData("https://www.google.com/maps/place/Eiffel+Tower/@48.858,2.294,17z/data=xyz", "Eiffel Tower")]
    [InlineData("https://www.google.com/maps/place/Caf%C3%A9+de+Flore/@48.854,2.332,17z", "Café de Flore")]
    [InlineData("https://www.google.com/maps/place/JustName", "JustName")]  // no trailing slash → full segment
    [InlineData("https://example.com/notmaps", null)]                        // no /place/ → null
    public void ExtractPlaceQuery_returns_expected(string url, string? expected)
    {
        var result = GooglePlaceResolver.ExtractPlaceQuery(url);
        result.Should().Be(expected);
    }
}
