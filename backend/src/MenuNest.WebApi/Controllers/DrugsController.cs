using Mediator;
using MenuNest.Application.UseCases.Health;
using MenuNest.Application.UseCases.Health.DrugMaster.AttachPhotosToDrug;
using MenuNest.Application.UseCases.Health.DrugMaster.CreateDrug;
using MenuNest.Application.UseCases.Health.DrugMaster.DeleteDrug;
using MenuNest.Application.UseCases.Health.DrugMaster.GetDrug;
using MenuNest.Application.UseCases.Health.DrugMaster.ListDrugs;
using MenuNest.Application.UseCases.Health.DrugMaster.UpdateDrug;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/drugs")]
public sealed class DrugsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DrugsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DrugDto>>> List(
        [FromQuery] Guid? symptomId,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListDrugsQuery(symptomId), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DrugDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDrugQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<DrugDetailDto>> Create(
        [FromBody] CreateDrugCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DrugDetailDto>> Update(
        Guid id,
        [FromBody] UpdateDrugRequest request,
        CancellationToken ct)
    {
        var command = new UpdateDrugCommand(
            id,
            request.Name, request.DrugType, request.DoseStrength,
            request.EffectDurationMinHours, request.EffectDurationMaxHours,
            request.MaxDailyDose, request.StockCount,
            request.ActiveIngredient, request.ExpirationDate, request.UsageNote,
            request.TreatsSymptomIds);

        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteDrugCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/photos")]
    public async Task<ActionResult<IReadOnlyList<PhotoRefDto>>> AttachPhotos(
        Guid id,
        [FromBody] AttachPhotosRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new AttachPhotosToDrugCommand(id, request.Photos), ct);
        return Ok(result);
    }
}

public sealed record UpdateDrugRequest(
    string Name,
    MenuNest.Domain.Enums.DrugType DrugType,
    string DoseStrength,
    int EffectDurationMinHours,
    int EffectDurationMaxHours,
    int MaxDailyDose,
    int StockCount,
    string? ActiveIngredient,
    DateOnly? ExpirationDate,
    string? UsageNote,
    IReadOnlyList<Guid>? TreatsSymptomIds);

public sealed record AttachPhotosRequest(IReadOnlyList<AttachedPhotoInfo> Photos);
