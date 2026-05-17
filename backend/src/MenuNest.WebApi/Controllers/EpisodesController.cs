using Mediator;
using MenuNest.Application.UseCases.Health;
using MenuNest.Application.UseCases.Health.DrugMaster.AttachPhotosToDrug;
using MenuNest.Application.UseCases.Health.Episodes.AttachPhotosToEpisode;
using MenuNest.Application.UseCases.Health.Episodes.DeleteEpisode;
using MenuNest.Application.UseCases.Health.Episodes.GetActiveEpisodes;
using MenuNest.Application.UseCases.Health.Episodes.GetEpisode;
using MenuNest.Application.UseCases.Health.Episodes.ListEpisodes;
using MenuNest.Application.UseCases.Health.Episodes.ResolveEpisode;
using MenuNest.Application.UseCases.Health.Episodes.StartEpisode;
using MenuNest.Application.UseCases.Health.Episodes.UpdateEpisode;
using MenuNest.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/episodes")]
public sealed class EpisodesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EpisodesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EpisodeDto>>> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? symptomId,
        [FromQuery] bool? onlyResolved,
        [FromQuery] bool? onlyFailed,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ListEpisodesQuery(from, to, symptomId, onlyResolved, onlyFailed), ct);
        return Ok(result);
    }

    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<EpisodeDto>>> GetActive(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetActiveEpisodesQuery(), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EpisodeDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEpisodeQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<EpisodeDto>> Start(
        [FromBody] StartEpisodeCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EpisodeDetailDto>> Update(
        Guid id,
        [FromBody] UpdateEpisodeRequest request,
        CancellationToken ct)
    {
        var command = new UpdateEpisodeCommand(
            Id: id,
            Severity: request.Severity,
            Notes: request.Notes,
            IsOnPeriod: request.IsOnPeriod,
            TriggerIds: request.TriggerIds,
            HasAura: request.HasAura,
            AuraTypes: request.AuraTypes,
            AuraDurationMin: request.AuraDurationMin,
            Location: request.Location,
            Quality: request.Quality,
            AssociatedSymptoms: request.AssociatedSymptoms,
            WorsenedByActivity: request.WorsenedByActivity,
            FunctionalImpact: request.FunctionalImpact,
            MigraineAttributesProvided: request.MigraineAttributesProvided);

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/resolve")]
    public async Task<ActionResult<EpisodeDetailDto>> Resolve(
        Guid id,
        [FromBody] ResolveEpisodeRequest? request,
        CancellationToken ct)
    {
        var command = new ResolveEpisodeCommand(
            Id: id,
            SeverityAfter: request?.SeverityAfter ?? 0,
            EndedAt: request?.EndedAt);

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteEpisodeCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/photos")]
    public async Task<ActionResult<IReadOnlyList<PhotoRefDto>>> AttachPhotos(
        Guid id,
        [FromBody] AttachEpisodePhotosRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AttachPhotosToEpisodeCommand(id, request.Photos), ct);
        return Ok(result);
    }
}

public sealed record UpdateEpisodeRequest(
    int? Severity,
    string? Notes,
    bool? IsOnPeriod,
    IReadOnlyList<Guid>? TriggerIds,
    bool? HasAura,
    IReadOnlyList<AuraType>? AuraTypes,
    int? AuraDurationMin,
    SymptomLocation? Location,
    SymptomQuality? Quality,
    IReadOnlyList<AssociatedSymptom>? AssociatedSymptoms,
    bool? WorsenedByActivity,
    FunctionalImpact? FunctionalImpact,
    bool MigraineAttributesProvided = false);

public sealed record ResolveEpisodeRequest(int SeverityAfter = 0, DateTime? EndedAt = null);

public sealed record AttachEpisodePhotosRequest(IReadOnlyList<AttachedPhotoInfo> Photos);
