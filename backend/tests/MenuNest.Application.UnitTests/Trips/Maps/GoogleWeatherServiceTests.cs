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

    // Returns _page1 for the first (token-less) forecast request and _page2 once the URL carries a
    // pageToken, so a test can prove the service follows Google's forecast pagination.
    private sealed class PagedForecastHandler : HttpMessageHandler
    {
        private readonly string _page1;
        private readonly string _page2;
        public int Calls { get; private set; }
        public string? LastPageToken { get; private set; } // the actual token value the service forwarded
        public PagedForecastHandler(string page1, string page2) { _page1 = page1; _page2 = page2; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var url = request.RequestUri!.ToString();
            var idx = url.IndexOf("pageToken=", StringComparison.Ordinal);
            if (idx >= 0) LastPageToken = Uri.UnescapeDataString(url[(idx + "pageToken=".Length)..]);
            var json = idx >= 0 ? _page2 : _page1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    // Always hands back a non-matching bucket plus a fresh nextPageToken, for _tokenPages responses,
    // then a token-less page — lets a test assert the pagination loop is bounded (does not run away).
    private sealed class EndlessTokenHandler : HttpMessageHandler
    {
        private readonly int _tokenPages;
        public int Calls { get; private set; }
        public EndlessTokenHandler(int tokenPages) { _tokenPages = tokenPages; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var bucket = "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":13}," +
                "\"weatherCondition\":{\"type\":\"CLOUDY\"},\"temperature\":{\"degrees\":31.0}}";
            var token = Calls <= _tokenPages ? ",\"nextPageToken\":\"P" + Calls + "\"" : "";
            var json = "{\"forecastHours\":[" + bucket + "]" + token + "}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
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
    public async Task Now_parses_uv_index_and_feels_like()
    {
        const string json =
            "{\"weatherCondition\":{\"type\":\"CLEAR\"}," +
            "\"temperature\":{\"degrees\":34.0}," +
            "\"feelsLikeTemperature\":{\"unit\":\"CELSIUS\",\"degrees\":39.4}," +
            "\"uvIndex\":9}";
        var svc = Build(new StubHandler(HttpStatusCode.OK, json));

        var r = (await svc.GetReadingsAsync(OnePoint, WeatherReadingKind.Now, CancellationToken.None))[0];

        r.HasData.Should().BeTrue();
        r.UvIndex.Should().Be(9);
        r.FeelsLikeC.Should().Be(39.4);
    }

    [Fact]
    public async Task OnArrival_parses_uv_index_and_feels_like()
    {
        const string json =
            "{\"forecastHours\":[" +
            "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":14}," +
            "\"weatherCondition\":{\"type\":\"CLEAR\"},\"temperature\":{\"degrees\":31.0}," +
            "\"feelsLikeTemperature\":{\"degrees\":35.2},\"uvIndex\":2}]}";
        var svc = Build(new StubHandler(HttpStatusCode.OK, json));
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 14, 30, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.UvIndex.Should().Be(2);
        r.FeelsLikeC.Should().Be(35.2);
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

    [Fact]
    public async Task Now_duplicate_coordinates_in_one_batch_each_keep_their_own_StopId()
    {
        const string json =
            "{\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/cloudy\"," +
            "\"description\":{\"text\":\"มีเมฆมาก\",\"languageCode\":\"th\"},\"type\":\"CLOUDY\"}," +
            "\"temperature\":{\"unit\":\"CELSIUS\",\"degrees\":29.1}," +
            "\"precipitation\":{\"probability\":{\"type\":\"RAIN\",\"percent\":20}}}";
        var svc = Build(new StubHandler(HttpStatusCode.OK, json));
        var pts = new List<WeatherPoint>
        {
            new("s1", 13.7563, 100.5018, null),
            new("s2", 13.7563, 100.5018, null),
        };

        var readings = await svc.GetReadingsAsync(pts, WeatherReadingKind.Now, CancellationToken.None);

        readings[0].StopId.Should().Be("s1");
        readings[0].HasData.Should().BeTrue();
        readings[1].StopId.Should().Be("s2"); // cache-hit path (same coords) must re-stamp, not reuse s1's cached reading
        readings[1].HasData.Should().BeTrue();
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
    public async Task OnArrival_follows_next_page_token_when_arrival_is_beyond_the_first_page()
    {
        // Google's forecast/hours returns only 24 buckets per page; an arrival >~24h out lives on a
        // later page reachable only via nextPageToken. Regression for the "ไม่มีข้อมูลอากาศ" bug.
        const string page1 =
            "{\"forecastHours\":[" +
            "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":13}," +
            "\"weatherCondition\":{\"type\":\"CLOUDY\"},\"temperature\":{\"degrees\":31.0},\"precipitation\":{\"probability\":{\"percent\":30}}}," +
            "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":14}," +
            "\"weatherCondition\":{\"type\":\"LIGHT_RAIN\"},\"temperature\":{\"degrees\":30.0},\"precipitation\":{\"probability\":{\"percent\":55}}}" +
            "],\"nextPageToken\":\"PAGE2\"}";
        const string page2 =
            "{\"forecastHours\":[" +
            "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":20}," +
            "\"weatherCondition\":{\"iconBaseUri\":\"https://maps.gstatic.com/weather/v1/clear\",\"description\":{\"text\":\"ท้องฟ้าแจ่มใส\"},\"type\":\"CLEAR\"}," +
            "\"temperature\":{\"degrees\":27.0},\"precipitation\":{\"probability\":{\"percent\":10}}}" +
            "]}";
        var handler = new PagedForecastHandler(page1, page2);
        var svc = Build(handler);
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 20, 41, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeTrue();
        r.ConditionType.Should().Be("CLEAR"); // the 20:00 bucket, found only after following the page token
        r.TempC.Should().Be(27.0);
        r.RainPct.Should().Be(10);
        handler.Calls.Should().Be(2); // page 1 (miss) then page 2 (hit)
        handler.LastPageToken.Should().Be("PAGE2"); // the exact nextPageToken from page 1 was forwarded
    }

    [Fact]
    public async Task OnArrival_pagination_is_bounded_when_no_bucket_ever_matches()
    {
        // Guard against a runaway loop: even if Google keeps handing back nextPageToken, the service
        // must stop after a bounded number of pages (10 days / 24h ≈ 10 pages) and degrade to No-data.
        var handler = new EndlessTokenHandler(tokenPages: 50);
        var svc = Build(handler);
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 20, 20, 0, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
        handler.Calls.Should().BeLessThanOrEqualTo(11); // 10 pages of 24h covers the 240h horizon, +1 slack
    }

    [Fact]
    public async Task OnArrival_stops_paging_once_the_page_window_reaches_the_arrival_hour()
    {
        // Buckets are chronological: if this page already reaches the arrival hour but has no exact
        // match (a gap, or an arrival that drifted into the past), no later page can contain it — so
        // the service must stop, not burn the remaining pages every render for a permanently No-data stop.
        const string page1 =
            "{\"forecastHours\":[" +
            "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":13},\"weatherCondition\":{\"type\":\"CLOUDY\"},\"temperature\":{\"degrees\":31.0}}," +
            "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":14},\"weatherCondition\":{\"type\":\"CLOUDY\"},\"temperature\":{\"degrees\":31.0}}," +
            "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":16},\"weatherCondition\":{\"type\":\"CLOUDY\"},\"temperature\":{\"degrees\":31.0}}" +
            "],\"nextPageToken\":\"PAGE2\"}";
        // page 2 WOULD satisfy the 15:00 arrival — if the service wrongly followed the token, the bug shows
        // as HasData=true and Calls=2 instead of the correct early stop.
        const string page2 =
            "{\"forecastHours\":[" +
            "{\"displayDateTime\":{\"year\":2026,\"month\":7,\"day\":12,\"hours\":15},\"weatherCondition\":{\"type\":\"CLEAR\"},\"temperature\":{\"degrees\":27.0}}" +
            "]}";
        var handler = new PagedForecastHandler(page1, page2);
        var svc = Build(handler);
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, new DateTime(2026, 7, 12, 15, 30, 0)) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
        handler.Calls.Should().Be(1); // stopped after the page whose window already passed the arrival hour
    }

    [Fact]
    public async Task OnArrival_with_null_arrival_makes_no_request()
    {
        // A null arrival can never match any forecast hour bucket, so short-circuit to No-data without
        // paging Google (the SPA always sends arrivalIso, but a programmatic/MCP caller could omit it).
        var handler = new PagedForecastHandler(ForecastJson, ForecastJson);
        var svc = Build(handler);
        var pts = new List<WeatherPoint> { new("s1", 13.7563, 100.5018, null) };

        var r = (await svc.GetReadingsAsync(pts, WeatherReadingKind.OnArrival, CancellationToken.None))[0];

        r.HasData.Should().BeFalse();
        handler.Calls.Should().Be(0); // guarded: no HTTP call at all for a null arrival
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
