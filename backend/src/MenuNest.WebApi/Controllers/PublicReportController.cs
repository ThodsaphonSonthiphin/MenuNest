using Mediator;
using MenuNest.Application.UseCases.Health;
using MenuNest.Application.UseCases.Health.Reports.GetDoctorReport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

/// <summary>
/// Token-gated public endpoint that the doctor opens after scanning the
/// QR code. We deliberately keep token verification + revocation checks
/// inside <see cref="GetDoctorReportHandler"/> rather than introducing a
/// separate action filter — co-locating the security decisions with the
/// data fetch makes the contract easier to audit and avoids the filter
/// running queries that the handler then redoes.
/// </summary>
[ApiController]
[Route("api/public")]
[AllowAnonymous]
public sealed class PublicReportController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicReportController(IMediator mediator) => _mediator = mediator;

    [HttpGet("report")]
    public async Task<ActionResult<DoctorReportDto>> GetReport(
        [FromQuery] string t,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDoctorReportQuery(t), ct);
        return Ok(result);
    }
}
