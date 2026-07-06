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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        public HttpRequestMessage? LastRequest { get; private set; }
        public RecordingHandler(HttpStatusCode status, string json = "") { _status = status; _json = json; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
        }
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

    [Fact]
    public async Task Now_request_sends_no_field_mask_and_hits_current_conditions()
    {
        const string json =
            "{\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/cloudy\"," +
            "\"description\":{\"text\":\"มีเมฆมาก\",\"languageCode\":\"th\"},\"type\":\"CLOUDY\"}," +
            "\"temperature\":{\"unit\":\"CELSIUS\",\"degrees\":29.1}," +
            "\"precipitation\":{\"probability\":{\"type\":\"RAIN\",\"percent\":20}}}";
        var handler = new RecordingHandler(HttpStatusCode.OK, json);
        var svc = Build(handler);

        await svc.GetReadingsAsync(OnePoint, WeatherReadingKind.Now, CancellationToken.None);

        var request = handler.LastRequest;
        request.Should().NotBeNull();
        request!.Headers.Contains("X-Goog-FieldMask").Should().BeFalse();
        request.Headers.Contains("X-Goog-Api-Key").Should().BeTrue();
        request.RequestUri!.ToString().Should().Contain("currentConditions:lookup");
        request.RequestUri!.ToString().Should().Contain("unitsSystem=METRIC");
        request.RequestUri!.ToString().Should().Contain("languageCode=th");
    }

    private const string ForecastJson =
        "{\"forecastHours\":[" +
        "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":13}," +
        "\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/cloudy\",\"description\":{\"text\":\"มีเมฆมาก\"},\"type\":\"CLOUDY\"}," +
        "\"temperature\":{\"degrees\":31.0},\"precipitation\":{\"probability\":{\"percent\":30}}}," +
        "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":14}," +
        "\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/drizzle\",\"description\":{\"text\":\"ฝนตกเบาบาง\"},\"type\":\"LIGHT_RAIN\"}," +
        "\"temperature\":{\"degrees\":30.0},\"precipitation\":{\"probability\":{\"percent\":55}}}" +
        "]}";

    [Fact]
    public async Task OnArrival_picks_the_hour_bucket_matching_the_arrival_hour()
    {
        var svc = Build(new StubHandler(HttpStatusCode.OK, ForecastJson));
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 14, 30, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeTrue();
        r.ConditionType.Should().Be("LIGHT_RAIN"); // the 14:00 bucket, not the 13:00 one
        r.TempC.Should().Be(30.0);
        r.RainPct.Should().Be(55);
    }

    [Fact]
    public async Task OnArrival_with_no_matching_bucket_is_no_data()
    {
        var svc = Build(new StubHandler(HttpStatusCode.OK, ForecastJson));
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 20, 0, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
    }

    [Fact]
    public async Task OnArrival_failure_degrades_to_no_data()
    {
        var svc = Build(new StubHandler(HttpStatusCode.InternalServerError));
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 14, 0, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
    }

    [Fact]
    public async Task OnArrival_with_null_arrival_is_no_data() // tolerance: missing/late arrival ⇒ No-data, not an error (ADR-032)
    {
        var svc = Build(new StubHandler(HttpStatusCode.OK, ForecastJson));
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, null) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
    }
}
