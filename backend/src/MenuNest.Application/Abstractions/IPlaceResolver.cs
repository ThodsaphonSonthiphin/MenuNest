using MenuNest.Application.UseCases.Trips;
namespace MenuNest.Application.Abstractions;

public interface IPlaceResolver
{
    /// <summary>Resolve a shared Google Maps URL to authoritative place data via a live API.</summary>
    Task<ResolvedPlaceDto> ResolveFromUrlAsync(string url, CancellationToken ct);
}
