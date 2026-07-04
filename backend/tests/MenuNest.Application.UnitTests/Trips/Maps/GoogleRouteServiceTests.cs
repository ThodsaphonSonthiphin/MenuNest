using System.Net;
using System.Text;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Maps;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class GoogleRouteServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        public StubHandler(HttpStatusCode status, string json = "") { _status = status; _json = json; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static GoogleRouteService Build(HttpMessageHandler handler) => new(
        new StubFactory(handler),
        Options.Create(new GoogleMapsOptions { ApiKey = "test-key" }),
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<GoogleRouteService>.Instance);

    private static readonly List<RoutePoint> TwoPoints =
        new() { new(12.61, 102.10), new(12.57, 102.18) };

    [Fact]
    public async Task Routed_leg_parses_distance_duration_and_polyline()
    {
        const string json = "{\"routes\":[{\"duration\":\"1830s\",\"distanceMeters\":45200,\"polyline\":{\"encodedPolyline\":\"_p~iF~ps|U_ulLnnqC\"}}]}";
        var svc = Build(new StubHandler(HttpStatusCode.OK, json));

        var legs = await svc.GetLegTimesAsync(TwoPoints, TravelMode.Drive, CancellationToken.None);

        legs.Should().HaveCount(1);
        legs[0].Source.Should().Be(RouteSource.Routed);
        legs[0].Seconds.Should().Be(1830);
        legs[0].Meters.Should().Be(45200);
        legs[0].EncodedPolyline.Should().Be("_p~iF~ps|U_ulLnnqC");
    }

    [Fact]
    public async Task Google_failure_falls_back_to_Estimated_without_polyline()
    {
        var svc = Build(new StubHandler(HttpStatusCode.Forbidden, "{\"error\":{\"status\":\"PERMISSION_DENIED\"}}"));

        var legs = await svc.GetLegTimesAsync(TwoPoints, TravelMode.Drive, CancellationToken.None);

        legs.Should().HaveCount(1);
        legs[0].Source.Should().Be(RouteSource.Estimated);
        legs[0].EncodedPolyline.Should().BeNull();
        legs[0].Meters.Should().BeGreaterThan(0); // Haversine still produced an estimate
    }
}
