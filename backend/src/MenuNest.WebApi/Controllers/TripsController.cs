using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.CreateTrip;
using MenuNest.Application.UseCases.Trips.DeleteTrip;
using MenuNest.Application.UseCases.Trips.ListTrips;
using MenuNest.Application.UseCases.Trips.UpdateTrip;
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
}

public sealed record UpdateTripBody(
    string Name, string? Destination, DateOnly StartDate, int DayCount,
    MenuNest.Domain.Enums.TravelMode DefaultTravelMode);
