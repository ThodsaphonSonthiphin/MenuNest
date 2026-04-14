using Mediator;
using MenuNest.Application.UseCases.Families;
using MenuNest.Application.UseCases.Families.CreateFamily;
using MenuNest.Application.UseCases.Families.JoinFamily;
using MenuNest.Application.UseCases.Families.ListFamilyMembers;
using MenuNest.Application.UseCases.Families.RotateInviteCode;
using MenuNest.Application.UseCases.Families.LeaveFamily;
using MenuNest.Application.UseCases.Families.AddRelationship;
using MenuNest.Application.UseCases.Families.DeleteRelationship;
using MenuNest.Application.UseCases.Families.ListRelationships;
using MenuNest.Domain.Enums;
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

    [HttpPost("join")]
    public async Task<ActionResult<FamilyDto>> Join(
        [FromBody] JoinFamilyRequest request,
        CancellationToken ct)
    {
        var family = await _mediator.Send(new JoinFamilyCommand(request.InviteCode), ct);
        return Ok(family);
    }

    [HttpGet("me/members")]
    public async Task<ActionResult<IReadOnlyList<FamilyMemberDto>>> ListMembers(CancellationToken ct)
    {
        var members = await _mediator.Send(new ListFamilyMembersQuery(), ct);
        return Ok(members);
    }

    [HttpPost("me/invite-codes/rotate")]
    public async Task<ActionResult<RotateInviteCodeResult>> RotateInviteCode(CancellationToken ct)
    {
        var result = await _mediator.Send(new RotateInviteCodeCommand(), ct);
        return Ok(result);
    }

    [HttpPost("leave")]
    public async Task<IActionResult> Leave(CancellationToken ct)
    {
        await _mediator.Send(new LeaveFamilyCommand(), ct);
        return NoContent();
    }

    [HttpGet("me/relationships")]
    public async Task<ActionResult<IReadOnlyList<RelationshipDto>>> ListRelationships(CancellationToken ct)
    {
        var items = await _mediator.Send(new ListRelationshipsQuery(), ct);
        return Ok(items);
    }

    [HttpPost("me/relationships")]
    public async Task<ActionResult<RelationshipDto>> AddRelationship(
        [FromBody] AddRelationshipRequest request,
        CancellationToken ct)
    {
        var dto = await _mediator.Send(
            new AddRelationshipCommand(request.FromUserId, request.ToUserId, request.RelationType), ct);
        return Ok(dto);
    }

    [HttpDelete("me/relationships/{id:guid}")]
    public async Task<IActionResult> DeleteRelationship(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteRelationshipCommand(id), ct);
        return NoContent();
    }
}

public sealed record CreateFamilyRequest(string Name);
public sealed record JoinFamilyRequest(string InviteCode);
public sealed record AddRelationshipRequest(Guid FromUserId, Guid ToUserId, RelationType RelationType);
