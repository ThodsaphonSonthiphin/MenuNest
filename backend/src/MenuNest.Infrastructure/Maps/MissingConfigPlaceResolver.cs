using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Domain.Exceptions;
namespace MenuNest.Infrastructure.Maps;

/// <summary>Registered when no Maps API key is configured — fail with a clear message.</summary>
public sealed class MissingConfigPlaceResolver : IPlaceResolver
{
    public Task<ResolvedPlaceDto> ResolveFromUrlAsync(string url, CancellationToken ct)
        => throw new DomainException("Maps link resolving is not configured. Add the place manually.");
}
