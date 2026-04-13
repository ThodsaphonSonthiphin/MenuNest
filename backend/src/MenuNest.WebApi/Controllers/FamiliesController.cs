using Mediator;
using MenuNest.Application.UseCases.Families;
using MenuNest.Application.UseCases.Families.CreateFamily;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/families")]
public sealed class FamiliesController : ControllerBase
{
    private readonly IMediator _mediator;

    public FamiliesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Creates a new family and enrols the caller as its first member.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<FamilyDto>> Create(
        [FromBody] CreateFamilyRequest request,
        CancellationToken ct)
    {
        var family = await _mediator.Send(new CreateFamilyCommand(request.Name), ct);
        return CreatedAtAction(nameof(Create), new { id = family.Id }, family);
    }
}

public sealed record CreateFamilyRequest(string Name);
