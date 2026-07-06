using System.Linq;
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Infrastructure;
using MenuNest.Infrastructure.Maps;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MenuNest.Application.UnitTests.Trips.Maps;

public class WeatherServiceRegistrationTests
{
    private static Type ResolveWeatherImpl(string? mapsKey)
    {
        // AddInfrastructure reads ConnectionStrings:DefaultConnection at registration and throws
        // if it is missing; seed any non-empty value (UseSqlServer does not connect here). This is
        // the ONLY config key required at registration time.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=test;Database=Test;Trusted_Connection=True;",
        };
        if (mapsKey is not null) settings["GoogleMaps:ApiKey"] = mapsKey;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(config);

        return services.Single(d => d.ServiceType == typeof(IWeatherService)).ImplementationType!;
    }

    [Fact]
    public void Uses_Google_when_maps_key_present()
        => ResolveWeatherImpl("test-key").Should().Be<GoogleWeatherService>();

    [Fact]
    public void Uses_noop_when_maps_key_absent()
        => ResolveWeatherImpl(null).Should().Be<MissingConfigWeatherService>();
}
