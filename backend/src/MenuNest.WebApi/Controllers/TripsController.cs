using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Application.UseCases.Trips.CreateTrip;
using MenuNest.Application.UseCases.Trips.DeleteTrip;
using MenuNest.Application.UseCases.Trips.DeleteTripPlace;
using MenuNest.Application.UseCases.Trips.ListTripPlaces;
using MenuNest.Application.UseCases.Trips.ListTrips;
using MenuNest.Application.UseCases.Trips.ResolvePlace;
using MenuNest.Application.UseCases.Trips.UpdateTrip;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
public sealed class TripsController : ControllerBase
{
    private readonly IMediator _mediator;
    public TripsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/trips")]
    public async Task<ActionResult<IReadOnlyList<TripDto>>> List(CancellationToken ct)
        => Ok(await _mediator.Send(new ListTripsQuery(), ct));

    [HttpPost("api/trips")]
    public async Task<ActionResult<TripDto>> Create([FromBody] CreateTripCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));

    [HttpPut("api/trips/{id:guid}")]
    public async Task<ActionResult<TripDto>> Update(Guid id, [FromBody] UpdateTripBody body, CancellationToken ct)
        => Ok(await _mediator.Send(new UpdateTripCommand(id, body.Name, body.Destination, body.StartDate, body.DayCount, body.DefaultTravelMode), ct));

    [HttpDelete("api/trips/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteTripCommand(id), ct);
        return NoContent();
    }

    [HttpPost("api/trips/resolve-place")]
    public async Task<ActionResult<ResolvedPlaceDto>> Resolve([FromBody] ResolvePlaceCommand cmd, CancellationToken ct)
        => Ok(await _mediator.Send(cmd, ct));

    [HttpGet("api/trips/{id:guid}/places")]
    public async Task<ActionResult<IReadOnlyList<TripPlaceDto>>> ListPlaces(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new ListTripPlacesQuery(id), ct));

    [HttpPost("api/trips/{id:guid}/places")]
    public async Task<ActionResult<TripPlaceDto>> AddPlace(Guid id, [FromBody] AddPlaceBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new AddTripPlaceCommand(id, b.Name, b.Lat, b.Lng, b.Category,
            b.GooglePlaceId, b.Address, b.PriceLevel, b.PhotoUrl, b.OpeningHoursJson), ct));

    [HttpPut("api/trips/{id:guid}/places/{placeId:guid}")]
    public async Task<ActionResult<TripPlaceDto>> UpdatePlace(Guid id, Guid placeId, [FromBody] UpdatePlaceBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new UpdateTripPlaceCommand(id, placeId, b.Name, b.Category, b.Address, b.FeeNote, b.Notes, b.BestTimeStart, b.BestTimeEnd), ct));

    [HttpDelete("api/trips/{id:guid}/places/{placeId:guid}")]
    public async Task<IActionResult> DeletePlace(Guid id, Guid placeId, CancellationToken ct)
    { await _mediator.Send(new DeleteTripPlaceCommand(id, placeId), ct); return NoContent(); }
}

public sealed record UpdateTripBody(
    string Name, string? Destination, DateOnly StartDate, int DayCount,
    MenuNest.Domain.Enums.TravelMode DefaultTravelMode);

public sealed record AddPlaceBody(
    string Name, double Lat, double Lng, PlaceCategory Category,
    string? GooglePlaceId, string? Address, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson);

public sealed record UpdatePlaceBody(
    string Name, PlaceCategory Category, string? Address, string? FeeNote, string? Notes,
    TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd);
