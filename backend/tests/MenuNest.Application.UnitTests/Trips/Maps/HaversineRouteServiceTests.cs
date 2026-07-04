using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.Maps;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class HaversineRouteServiceTests
{
    [Fact]
    public async Task Returns_one_leg_fewer_than_points_with_positive_times()
    {
        var svc = new HaversineRouteService();
        var pts = new List<RoutePoint> { new(18.80, 98.92), new(18.79, 98.99), new(18.77, 99.00) };
        var legs = await svc.GetLegTimesAsync(pts, TravelMode.Drive, CancellationToken.None);
        legs.Should().HaveCount(2);
        legs[0].Meters.Should().BeGreaterThan(0);
        legs[0].Seconds.Should().BeGreaterThan(0);
        legs[0].Source.Should().Be(RouteSource.Estimated);
        legs[0].EncodedPolyline.Should().BeNull();
    }

    [Fact]
    public async Task Empty_or_single_point_returns_no_legs()
    {
        var svc = new HaversineRouteService();
        (await svc.GetLegTimesAsync(new List<RoutePoint> { new(0, 0) }, TravelMode.Walk, default)).Should().BeEmpty();
    }
}
