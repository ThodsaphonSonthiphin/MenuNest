using Mediator;
using MenuNest.Application.UseCases.Places;
using MenuNest.Application.UseCases.Places.ListMyPlaces;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PlacesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlacesController(IMediator mediator) => _mediator = mediator;

    /// <summary>All the caller's saved Places across every Trip (deduped) for Discover.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DiscoverPlaceDto>>> ListMyPlaces(CancellationToken ct)
        => Ok(await _mediator.Send(new ListMyPlacesQuery(), ct));
}
