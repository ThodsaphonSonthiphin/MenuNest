using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Maps;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class MissingConfigWeatherServiceTests
{
    [Fact]
    public async Task Returns_no_data_for_every_point_and_never_throws()
    {
        var svc = new MissingConfigWeatherService();
        var points = new List<WeatherPoint> { new("s1", 13.7, 100.5, null), new("s2", 18.8, 98.9, null) };

        var readings = await svc.GetReadingsAsync(points, WeatherReadingKind.Now, CancellationToken.None);

        readings.Should().HaveCount(2);
        readings.Should().OnlyContain(r => r.HasData == false && r.ConditionType == null && r.TempC == null);
        readings.Select(r => r.StopId).Should().Equal("s1", "s2");
    }
}
