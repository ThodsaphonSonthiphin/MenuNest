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

public class GoogleWeatherServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        public StubHandler(HttpStatusCode status, string json = "") { _status = status; _json = json; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status)
            { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static GoogleWeatherService Build(HttpMessageHandler handler) => new(
        new StubFactory(handler),
        Options.Create(new GoogleMapsOptions { ApiKey = "test-key" }),
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<GoogleWeatherService>.Instance);

    private static readonly List<WeatherPoint> OnePoint = new() { new("s1", 13.7563, 100.5018, null) };

    [Fact]
    public async Task Now_parses_condition_temperature_and_rain()
    {
        const string json =
            "{\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/cloudy\"," +
            "\"description\":{\"text\":\"มีเมฆมาก\",\"languageCode\":\"th\"},\"type\":\"CLOUDY\"}," +
            "\"temperature\":{\"unit\":\"CELSIUS\",\"degrees\":29.1}," +
            "\"precipitation\":{\"probability\":{\"type\":\"RAIN\",\"percent\":20}}}";
        var svc = Build(new StubHandler(HttpStatusCode.OK, json));

        var r = (await svc.GetReadingsAsync(OnePoint, WeatherReadingKind.Now, CancellationToken.None))[0];

        r.HasData.Should().BeTrue();
        r.StopId.Should().Be("s1");
        r.ConditionType.Should().Be("CLOUDY");
        r.IconBaseUri.Should().Be("https://maps.gstatic.com/weather/v1/cloudy");
        r.TempC.Should().Be(29.1);
        r.RainPct.Should().Be(20);
        r.Description.Should().Be("มีเมฆมาก");
    }

    [Fact]
    public async Task Now_failure_degrades_to_no_data()
    {
        var svc = Build(new StubHandler(HttpStatusCode.Forbidden, "{\"error\":{\"status\":\"PERMISSION_DENIED\"}}"));

        var r = (await svc.GetReadingsAsync(OnePoint, WeatherReadingKind.Now, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
        r.StopId.Should().Be("s1");
        r.ConditionType.Should().BeNull();
        r.TempC.Should().BeNull();
    }
}
