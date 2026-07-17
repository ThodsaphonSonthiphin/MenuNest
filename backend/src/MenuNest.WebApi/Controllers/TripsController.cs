using Mediator;
using MenuNest.Application.UseCases.Trips;
using MenuNest.Application.UseCases.Trips.AddStop;
using MenuNest.Application.UseCases.Trips.AddTripPlace;
using MenuNest.Application.UseCases.Trips.AttachChecklistItem;
using MenuNest.Application.UseCases.Trips.CreateTrip;
using MenuNest.Application.UseCases.Trips.DetachChecklistItem;
using MenuNest.Application.UseCases.Trips.DeleteTrip;
using MenuNest.Application.UseCases.Trips.DeleteTripPlace;
using MenuNest.Application.UseCases.Trips.GetItinerary;
using MenuNest.Application.UseCases.Trips.GetStopWeather;
using MenuNest.Application.UseCases.Trips.GetTrip;
using MenuNest.Application.UseCases.Trips.ListChecklistItems;
using MenuNest.Application.UseCases.Trips.ListTripPlaces;
using MenuNest.Application.UseCases.Trips.ListTrips;
using MenuNest.Application.UseCases.Trips.RemoveStop;
using MenuNest.Application.UseCases.Trips.ReorderStops;
using MenuNest.Application.UseCases.Trips.ResolvePlace;
using MenuNest.Application.UseCases.Trips.SetChecklistEntryChecked;
using MenuNest.Application.UseCases.Trips.SetDayStartTime;
using MenuNest.Application.UseCases.Trips.SetDayUseCurrentTime;
using MenuNest.Application.UseCases.Trips.UpdateStop;
using MenuNest.Application.UseCases.Trips.UpdateTrip;
using MenuNest.Application.UseCases.Trips.UpdateTripPlace;
using MenuNest.Application.UseCases.Trips.PushPlaceProfile;
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

    [HttpGet("api/trips/{id:guid}")]
    public async Task<ActionResult<TripDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetTripQuery(id), ct));

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
        => Ok(await _mediator.Send(new UpdateTripPlaceCommand(id, placeId, b.Name, b.Category, b.Address, b.FeeNote, b.Notes, b.BestTimeStart, b.BestTimeEnd, b.ReviewLinks, b.SeasonPeriods), ct));

    [HttpDelete("api/trips/{id:guid}/places/{placeId:guid}")]
    public async Task<IActionResult> DeletePlace(Guid id, Guid placeId, CancellationToken ct)
    { await _mediator.Send(new DeleteTripPlaceCommand(id, placeId), ct); return NoContent(); }

    [HttpPost("api/trips/{id:guid}/places/{placeId:guid}/push-to-profile")]
    public async Task<ActionResult<TripPlaceDto>> PushPlaceProfile(Guid id, Guid placeId, CancellationToken ct)
        => Ok(await _mediator.Send(new PushPlaceProfileCommand(id, placeId), ct));

    [HttpGet("api/checklist-items")]
    public async Task<ActionResult<IReadOnlyList<ChecklistItemDto>>> ListChecklistItems(CancellationToken ct)
        => Ok(await _mediator.Send(new ListChecklistItemsQuery(), ct));

    [HttpPost("api/trips/{id:guid}/places/{placeId:guid}/checklist")]
    public async Task<ActionResult<PlaceChecklistEntryDto>> AttachChecklistItem(Guid id, Guid placeId, [FromBody] AttachChecklistBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new AttachChecklistItemCommand(id, placeId, b.Name), ct));

    [HttpDelete("api/trips/{id:guid}/places/{placeId:guid}/checklist/{entryId:guid}")]
    public async Task<IActionResult> DetachChecklistItem(Guid id, Guid placeId, Guid entryId, CancellationToken ct)
    { await _mediator.Send(new DetachChecklistItemCommand(id, placeId, entryId), ct); return NoContent(); }

    [HttpPatch("api/trips/{id:guid}/places/{placeId:guid}/checklist/{entryId:guid}")]
    public async Task<ActionResult<PlaceChecklistEntryDto>> SetChecklistItemChecked(Guid id, Guid placeId, Guid entryId, [FromBody] SetChecklistCheckedBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new SetChecklistEntryCheckedCommand(id, placeId, entryId, b.IsChecked), ct));

    [HttpGet("api/trips/{id:guid}/itinerary")]
    public async Task<ActionResult<IReadOnlyList<ItineraryDayDto>>> GetItinerary(
        Guid id, [FromQuery] string? tz, [FromQuery] double? lat, [FromQuery] double? lng, CancellationToken ct)
        => Ok(await _mediator.Send(new GetItineraryQuery(id, tz, lat, lng), ct));

    [HttpPost("api/trips/{id:guid}/days/{dayId:guid}/stops")]
    public async Task<ActionResult<StopDto>> AddStop(Guid id, Guid dayId, [FromBody] AddStopBody b, CancellationToken ct)
        => Ok(await _mediator.Send(new AddStopCommand(id, dayId, b.TripPlaceId, b.DwellMinutes, b.TravelModeToReach), ct));

    [HttpPatch("api/trips/{id:guid}/stops/{stopId:guid}")]
    public async Task<IActionResult> UpdateStop(Guid id, Guid stopId, [FromBody] UpdateStopBody b, CancellationToken ct)
    { await _mediator.Send(new UpdateStopCommand(id, stopId, b.DwellMinutes, b.TravelModeToReach, b.IsVisited), ct); return NoContent(); }

    [HttpDelete("api/trips/{id:guid}/stops/{stopId:guid}")]
    public async Task<IActionResult> RemoveStop(Guid id, Guid stopId, CancellationToken ct)
    { await _mediator.Send(new RemoveStopCommand(id, stopId), ct); return NoContent(); }

    [HttpPost("api/trips/{id:guid}/days/{dayId:guid}/reorder")]
    public async Task<IActionResult> Reorder(Guid id, Guid dayId, [FromBody] ReorderBody b, CancellationToken ct)
    { await _mediator.Send(new ReorderStopsCommand(id, dayId, b.OrderedStopIds), ct); return NoContent(); }

    [HttpPatch("api/trips/{id:guid}/days/{dayId:guid}")]
    public async Task<IActionResult> SetDayStart(Guid id, Guid dayId, [FromBody] SetDayStartBody b, CancellationToken ct)
    { await _mediator.Send(new SetDayStartTimeCommand(id, dayId, b.StartTime), ct); return NoContent(); }

    [HttpPatch("api/trips/{id:guid}/days/{dayId:guid}/use-current-time")]
    public async Task<IActionResult> SetDayUseCurrentTime(Guid id, Guid dayId, [FromBody] SetDayUseCurrentTimeBody b, CancellationToken ct)
    { await _mediator.Send(new SetDayUseCurrentTimeCommand(id, dayId, b.UseCurrentTime), ct); return NoContent(); }

    [HttpPost("api/trips/weather")]
    public async Task<ActionResult<IReadOnlyList<WeatherReadingDto>>> Weather([FromBody] GetStopWeatherQuery q, CancellationToken ct)
        => Ok(await _mediator.Send(q, ct));
}

public sealed record UpdateTripBody(
    string Name, string? Destination, DateOnly StartDate, int DayCount,
    MenuNest.Domain.Enums.TravelMode DefaultTravelMode);

public sealed record AddPlaceBody(
    string Name, double Lat, double Lng, PlaceCategory Category,
    string? GooglePlaceId, string? Address, int? PriceLevel, string? PhotoUrl, string? OpeningHoursJson);

public sealed record AttachChecklistBody(string Name);

public sealed record SetChecklistCheckedBody(bool IsChecked);

public sealed record UpdatePlaceBody(
    string Name, PlaceCategory Category, string? Address, string? FeeNote, string? Notes,
    TimeOnly? BestTimeStart, TimeOnly? BestTimeEnd,
    IReadOnlyList<ReviewLinkDto> ReviewLinks,
    IReadOnlyList<SeasonPeriodDto> SeasonPeriods);

public sealed record AddStopBody(
    Guid TripPlaceId, int DwellMinutes, TravelMode TravelModeToReach);

public sealed record UpdateStopBody(
    int? DwellMinutes, TravelMode? TravelModeToReach, bool? IsVisited);

public sealed record ReorderBody(IReadOnlyList<Guid> OrderedStopIds);

public sealed record SetDayStartBody(TimeOnly StartTime);

public sealed record SetDayUseCurrentTimeBody(bool UseCurrentTime);
