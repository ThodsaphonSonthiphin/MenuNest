# Family Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the Family Management features — join via invite code (with QR), member list, relationships CRUD, invite code rotation, and leave family.

**Architecture:** Backend CQRS handlers (martinothamar/Mediator) → FamiliesController → RTK Query endpoints → Syncfusion FamilyPage UI. Domain layer (entities, enums, value objects, EF configs) is already complete — this plan adds handlers, controller endpoints, and frontend.

**Tech Stack:** ASP.NET 10 (Clean Architecture, Mediator, FluentValidation), React 19, RTK Query, Syncfusion Pure React + ej2-react-barcode-generator, react-hook-form.

**Spec:** `docs/superpowers/specs/2026-04-14-family-management-design.md`

---

## File Map

### Backend — New Files

| File | Purpose |
|------|---------|
| `Application/UseCases/Families/FamilyMemberDto.cs` | DTO for member list |
| `Application/UseCases/Families/RelationshipDto.cs` | DTO for relationship list |
| `Application/UseCases/Families/JoinFamily/JoinFamilyCommand.cs` | Command |
| `Application/UseCases/Families/JoinFamily/JoinFamilyHandler.cs` | Handler |
| `Application/UseCases/Families/JoinFamily/JoinFamilyValidator.cs` | FluentValidation |
| `Application/UseCases/Families/ListFamilyMembers/ListFamilyMembersQuery.cs` | Query |
| `Application/UseCases/Families/ListFamilyMembers/ListFamilyMembersHandler.cs` | Handler |
| `Application/UseCases/Families/RotateInviteCode/RotateInviteCodeCommand.cs` | Command |
| `Application/UseCases/Families/RotateInviteCode/RotateInviteCodeHandler.cs` | Handler |
| `Application/UseCases/Families/LeaveFamily/LeaveFamilyCommand.cs` | Command |
| `Application/UseCases/Families/LeaveFamily/LeaveFamilyHandler.cs` | Handler |
| `Application/UseCases/Families/AddRelationship/AddRelationshipCommand.cs` | Command |
| `Application/UseCases/Families/AddRelationship/AddRelationshipHandler.cs` | Handler |
| `Application/UseCases/Families/AddRelationship/AddRelationshipValidator.cs` | FluentValidation |
| `Application/UseCases/Families/DeleteRelationship/DeleteRelationshipCommand.cs` | Command |
| `Application/UseCases/Families/DeleteRelationship/DeleteRelationshipHandler.cs` | Handler |
| `Application/UseCases/Families/ListRelationships/ListRelationshipsQuery.cs` | Query |
| `Application/UseCases/Families/ListRelationships/ListRelationshipsHandler.cs` | Handler |

### Backend — Modified Files

| File | Change |
|------|--------|
| `WebApi/Controllers/FamiliesController.cs` | Add 6 new endpoints |

### Frontend — New Files

| File | Purpose |
|------|---------|
| `pages/family/hooks/useFamilyPage.ts` | Page-level logic: rotate, leave, error state |
| `pages/family/hooks/useAddRelationship.ts` | Dialog form: react-hook-form + mutation |

### Frontend — Modified Files

| File | Change |
|------|--------|
| `shared/api/api.ts` | Add 7 endpoints + 4 DTO interfaces |
| `pages/family/FamilyPage.tsx` | Full implementation (4 sections) |
| `pages/family/JoinFamilyPage.tsx` | Enable invite code input + `?code` query param |
| `pages/family/index.ts` | Update barrel exports |

---

## Task 1: Backend — Shared DTOs

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Families/FamilyMemberDto.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/RelationshipDto.cs`

- [ ] **Step 1: Create FamilyMemberDto**

```csharp
namespace MenuNest.Application.UseCases.Families;

public sealed record FamilyMemberDto(
    Guid UserId,
    string DisplayName,
    string Email,
    DateTime JoinedAt,
    bool IsCreator,
    RelationshipLabelDto[] Relationships);

public sealed record RelationshipLabelDto(
    Guid RelationshipId,
    string RelationType,
    string Label);
```

- [ ] **Step 2: Create RelationshipDto**

```csharp
namespace MenuNest.Application.UseCases.Families;

public sealed record RelationshipDto(
    Guid Id,
    Guid FromUserId,
    string FromUserName,
    Guid ToUserId,
    string ToUserName,
    string RelationType);
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build backend/src/MenuNest.Application/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Families/FamilyMemberDto.cs backend/src/MenuNest.Application/UseCases/Families/RelationshipDto.cs
git commit -m "feat(family): add FamilyMemberDto and RelationshipDto"
```

---

## Task 2: Backend — JoinFamily Handler

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Families/JoinFamily/JoinFamilyCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/JoinFamily/JoinFamilyValidator.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/JoinFamily/JoinFamilyHandler.cs`

- [ ] **Step 1: Create JoinFamilyCommand**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Families.JoinFamily;

public sealed record JoinFamilyCommand(string InviteCode) : ICommand<FamilyDto>;
```

- [ ] **Step 2: Create JoinFamilyValidator**

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.Families.JoinFamily;

public sealed class JoinFamilyValidator : AbstractValidator<JoinFamilyCommand>
{
    public JoinFamilyValidator()
    {
        RuleFor(x => x.InviteCode)
            .NotEmpty().WithMessage("Invite code is required.")
            .Matches(@"^[A-Z0-9]{4}-[A-Z0-9]{4}$").WithMessage("Invite code must be formatted as XXXX-XXXX.");
    }
}
```

- [ ] **Step 3: Create JoinFamilyHandler**

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.JoinFamily;

public sealed class JoinFamilyHandler : ICommandHandler<JoinFamilyCommand, FamilyDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<JoinFamilyCommand> _validator;

    public JoinFamilyHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<JoinFamilyCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<FamilyDto> Handle(JoinFamilyCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        if (user.FamilyId.HasValue)
            throw new DomainException("You already belong to a family. Leave it first before joining another.");

        var code = InviteCode.From(command.InviteCode);

        var family = await _db.Families
            .Include(f => f.Members)
            .FirstOrDefaultAsync(f => f.InviteCode == code, ct)
            ?? throw new DomainException("Invite code is invalid or expired.");

        user.JoinFamily(family.Id);
        await _db.SaveChangesAsync(ct);

        return new FamilyDto(
            Id: family.Id,
            Name: family.Name,
            InviteCode: family.InviteCode.Value,
            MemberCount: family.Members.Count);
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build backend/src/MenuNest.Application/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Families/JoinFamily/
git commit -m "feat(family): add JoinFamily handler with invite code lookup"
```

---

## Task 3: Backend — ListFamilyMembers Handler

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Families/ListFamilyMembers/ListFamilyMembersQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/ListFamilyMembers/ListFamilyMembersHandler.cs`

- [ ] **Step 1: Create ListFamilyMembersQuery**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Families.ListFamilyMembers;

public sealed record ListFamilyMembersQuery : IQuery<IReadOnlyList<FamilyMemberDto>>;
```

- [ ] **Step 2: Create ListFamilyMembersHandler**

This handler queries members and maps relationships to Thai labels.

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.ListFamilyMembers;

public sealed class ListFamilyMembersHandler
    : IQueryHandler<ListFamilyMembersQuery, IReadOnlyList<FamilyMemberDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListFamilyMembersHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<FamilyMemberDto>> Handle(
        ListFamilyMembersQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var family = await _db.Families
            .AsNoTracking()
            .FirstAsync(f => f.Id == familyId, ct);

        var members = await _db.Users
            .AsNoTracking()
            .Where(u => u.FamilyId == familyId)
            .OrderBy(u => u.JoinedAt)
            .ToListAsync(ct);

        var relationships = await _db.UserRelationships
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId)
            .ToListAsync(ct);

        return members.Select(m => new FamilyMemberDto(
            UserId: m.Id,
            DisplayName: m.DisplayName,
            Email: m.Email,
            JoinedAt: m.JoinedAt ?? m.CreatedAt,
            IsCreator: m.Id == family.CreatedByUserId,
            Relationships: relationships
                .Where(r => r.FromUserId == m.Id)
                .Select(r => new RelationshipLabelDto(
                    RelationshipId: r.Id,
                    RelationType: r.RelationType.ToString(),
                    Label: GetThaiLabel(r.RelationType)))
                .ToArray()
        )).ToList();
    }

    private static string GetThaiLabel(RelationType type) => type switch
    {
        RelationType.Parent => "พ่อ/แม่",
        RelationType.Child => "ลูก",
        RelationType.Spouse => "คู่สมรส",
        RelationType.Sibling => "พี่น้อง",
        _ => "อื่นๆ",
    };
}
```

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/MenuNest.Application/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Families/ListFamilyMembers/
git commit -m "feat(family): add ListFamilyMembers handler with Thai relationship labels"
```

---

## Task 4: Backend — RotateInviteCode + LeaveFamily Handlers

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Families/RotateInviteCode/RotateInviteCodeCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/RotateInviteCode/RotateInviteCodeHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/LeaveFamily/LeaveFamilyCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/LeaveFamily/LeaveFamilyHandler.cs`

- [ ] **Step 1: Create RotateInviteCodeCommand**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Families.RotateInviteCode;

public sealed record RotateInviteCodeCommand : ICommand<RotateInviteCodeResult>;

public sealed record RotateInviteCodeResult(string InviteCode);
```

- [ ] **Step 2: Create RotateInviteCodeHandler**

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.RotateInviteCode;

public sealed class RotateInviteCodeHandler
    : ICommandHandler<RotateInviteCodeCommand, RotateInviteCodeResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public RotateInviteCodeHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<RotateInviteCodeResult> Handle(
        RotateInviteCodeCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var family = await _db.Families.FirstAsync(f => f.Id == familyId, ct);
        var newCode = family.RotateInviteCode();
        await _db.SaveChangesAsync(ct);

        return new RotateInviteCodeResult(newCode.Value);
    }
}
```

- [ ] **Step 3: Create LeaveFamilyCommand**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Families.LeaveFamily;

public sealed record LeaveFamilyCommand : ICommand;
```

- [ ] **Step 4: Create LeaveFamilyHandler**

Cascade-deletes the user's relationships when leaving.

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.LeaveFamily;

public sealed class LeaveFamilyHandler : ICommandHandler<LeaveFamilyCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public LeaveFamilyHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(LeaveFamilyCommand command, CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        // Remove all relationships involving this user in this family
        var relationships = await _db.UserRelationships
            .Where(r => r.FamilyId == familyId
                        && (r.FromUserId == user.Id || r.ToUserId == user.Id))
            .ToListAsync(ct);

        _db.UserRelationships.RemoveRange(relationships);

        user.LeaveFamily();
        await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build backend/src/MenuNest.Application/`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Families/RotateInviteCode/ backend/src/MenuNest.Application/UseCases/Families/LeaveFamily/
git commit -m "feat(family): add RotateInviteCode and LeaveFamily handlers"
```

---

## Task 5: Backend — Relationship Handlers (Add, Delete, List)

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Families/AddRelationship/AddRelationshipCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/AddRelationship/AddRelationshipValidator.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/AddRelationship/AddRelationshipHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/DeleteRelationship/DeleteRelationshipCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/DeleteRelationship/DeleteRelationshipHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/ListRelationships/ListRelationshipsQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Families/ListRelationships/ListRelationshipsHandler.cs`

- [ ] **Step 1: Create AddRelationshipCommand**

```csharp
using Mediator;
using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Families.AddRelationship;

public sealed record AddRelationshipCommand(
    Guid FromUserId,
    Guid ToUserId,
    RelationType RelationType) : ICommand<RelationshipDto>;
```

- [ ] **Step 2: Create AddRelationshipValidator**

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.Families.AddRelationship;

public sealed class AddRelationshipValidator : AbstractValidator<AddRelationshipCommand>
{
    public AddRelationshipValidator()
    {
        RuleFor(x => x.FromUserId).NotEmpty().WithMessage("From user is required.");
        RuleFor(x => x.ToUserId).NotEmpty().WithMessage("To user is required.");
        RuleFor(x => x.RelationType).IsInEnum().WithMessage("Invalid relationship type.");
        RuleFor(x => x)
            .Must(x => x.FromUserId != x.ToUserId)
            .WithMessage("Cannot create a relationship between the same person.");
    }
}
```

- [ ] **Step 3: Create AddRelationshipHandler**

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.AddRelationship;

public sealed class AddRelationshipHandler
    : ICommandHandler<AddRelationshipCommand, RelationshipDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<AddRelationshipCommand> _validator;

    public AddRelationshipHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<AddRelationshipCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<RelationshipDto> Handle(
        AddRelationshipCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        // Verify both users belong to this family
        var fromUser = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == command.FromUserId && u.FamilyId == familyId, ct)
            ?? throw new DomainException("From user is not a member of this family.");

        var toUser = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == command.ToUserId && u.FamilyId == familyId, ct)
            ?? throw new DomainException("To user is not a member of this family.");

        var relationship = UserRelationship.Create(
            familyId, command.FromUserId, command.ToUserId, command.RelationType);

        _db.UserRelationships.Add(relationship);
        await _db.SaveChangesAsync(ct);

        return new RelationshipDto(
            Id: relationship.Id,
            FromUserId: relationship.FromUserId,
            FromUserName: fromUser.DisplayName,
            ToUserId: relationship.ToUserId,
            ToUserName: toUser.DisplayName,
            RelationType: relationship.RelationType.ToString());
    }
}
```

- [ ] **Step 4: Create DeleteRelationshipCommand**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Families.DeleteRelationship;

public sealed record DeleteRelationshipCommand(Guid Id) : ICommand;
```

- [ ] **Step 5: Create DeleteRelationshipHandler**

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.DeleteRelationship;

public sealed class DeleteRelationshipHandler : ICommandHandler<DeleteRelationshipCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteRelationshipHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteRelationshipCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var relationship = await _db.UserRelationships
            .FirstOrDefaultAsync(r => r.Id == command.Id && r.FamilyId == familyId, ct)
            ?? throw new DomainException("Relationship not found.");

        _db.UserRelationships.Remove(relationship);
        await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
```

- [ ] **Step 6: Create ListRelationshipsQuery**

```csharp
using Mediator;

namespace MenuNest.Application.UseCases.Families.ListRelationships;

public sealed record ListRelationshipsQuery : IQuery<IReadOnlyList<RelationshipDto>>;
```

- [ ] **Step 7: Create ListRelationshipsHandler**

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Families.ListRelationships;

public sealed class ListRelationshipsHandler
    : IQueryHandler<ListRelationshipsQuery, IReadOnlyList<RelationshipDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListRelationshipsHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<RelationshipDto>> Handle(
        ListRelationshipsQuery query, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var items = await _db.UserRelationships
            .AsNoTracking()
            .Where(r => r.FamilyId == familyId)
            .Join(_db.Users, r => r.FromUserId, u => u.Id,
                (r, from) => new { r, FromName = from.DisplayName })
            .Join(_db.Users, x => x.r.ToUserId, u => u.Id,
                (x, to) => new RelationshipDto(
                    x.r.Id,
                    x.r.FromUserId,
                    x.FromName,
                    x.r.ToUserId,
                    to.DisplayName,
                    x.r.RelationType.ToString()))
            .ToListAsync(ct);

        return items;
    }
}
```

- [ ] **Step 8: Build**

Run: `dotnet build backend/src/MenuNest.Application/`
Expected: Build succeeded

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Families/AddRelationship/ backend/src/MenuNest.Application/UseCases/Families/DeleteRelationship/ backend/src/MenuNest.Application/UseCases/Families/ListRelationships/
git commit -m "feat(family): add relationship handlers (Add, Delete, List)"
```

---

## Task 6: Backend — FamiliesController Endpoints

**Files:**
- Modify: `backend/src/MenuNest.WebApi/Controllers/FamiliesController.cs`

- [ ] **Step 1: Add all new endpoints to FamiliesController**

Add these using statements at the top of the file:

```csharp
using MenuNest.Application.UseCases.Families.JoinFamily;
using MenuNest.Application.UseCases.Families.ListFamilyMembers;
using MenuNest.Application.UseCases.Families.RotateInviteCode;
using MenuNest.Application.UseCases.Families.LeaveFamily;
using MenuNest.Application.UseCases.Families.AddRelationship;
using MenuNest.Application.UseCases.Families.DeleteRelationship;
using MenuNest.Application.UseCases.Families.ListRelationships;
using MenuNest.Domain.Enums;
```

Add these action methods inside the controller class (after the existing `Create` method):

```csharp
[HttpPost("join")]
public async Task<ActionResult<FamilyDto>> Join(
    [FromBody] JoinFamilyRequest request,
    CancellationToken ct)
{
    var family = await _mediator.Send(
        new JoinFamilyCommand(request.InviteCode), ct);
    return Ok(family);
}

[HttpGet("me/members")]
public async Task<ActionResult<IReadOnlyList<FamilyMemberDto>>> ListMembers(
    CancellationToken ct)
{
    var members = await _mediator.Send(new ListFamilyMembersQuery(), ct);
    return Ok(members);
}

[HttpPost("me/invite-codes/rotate")]
public async Task<ActionResult<RotateInviteCodeResult>> RotateInviteCode(
    CancellationToken ct)
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
public async Task<ActionResult<IReadOnlyList<RelationshipDto>>> ListRelationships(
    CancellationToken ct)
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
        new AddRelationshipCommand(
            request.FromUserId, request.ToUserId, request.RelationType), ct);
    return Ok(dto);
}

[HttpDelete("me/relationships/{id:guid}")]
public async Task<IActionResult> DeleteRelationship(Guid id, CancellationToken ct)
{
    await _mediator.Send(new DeleteRelationshipCommand(id), ct);
    return NoContent();
}
```

Add request records at the bottom of the file (next to the existing `CreateFamilyRequest`):

```csharp
public sealed record JoinFamilyRequest(string InviteCode);
public sealed record AddRelationshipRequest(Guid FromUserId, Guid ToUserId, RelationType RelationType);
```

- [ ] **Step 2: Build the full solution**

Run: `dotnet build backend/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/FamiliesController.cs
git commit -m "feat(family): add all Family Management endpoints to controller"
```

---

## Task 7: Frontend — RTK Query Endpoints + DTO Interfaces

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

- [ ] **Step 1: Add DTO interfaces**

Add these interfaces alongside the existing DTOs in `api.ts`:

```typescript
export interface FamilyMemberDto {
  userId: string
  displayName: string
  email: string
  joinedAt: string
  isCreator: boolean
  relationships: RelationshipLabelDto[]
}

export interface RelationshipLabelDto {
  relationshipId: string
  relationType: string
  label: string
}

export interface RelationshipDto {
  id: string
  fromUserId: string
  fromUserName: string
  toUserId: string
  toUserName: string
  relationType: string
}

export interface AddRelationshipRequest {
  fromUserId: string
  toUserId: string
  relationType: string
}
```

- [ ] **Step 2: Add tag types**

Add `'FamilyMembers'` and `'Relationships'` to the `tagTypes` array if not already present (check first — `FamilyMembers` may already exist from a prior session).

- [ ] **Step 3: Add endpoints inside the `endpoints` builder**

Add in the `// --- Family ---` section:

```typescript
joinFamily: build.mutation<FamilyDto, { inviteCode: string }>({
  query: (body) => ({ url: '/api/families/join', method: 'POST', body }),
  invalidatesTags: ['Me', 'Family'],
}),
listFamilyMembers: build.query<FamilyMemberDto[], void>({
  query: () => '/api/families/me/members',
  providesTags: ['FamilyMembers'],
}),
rotateInviteCode: build.mutation<{ inviteCode: string }, void>({
  query: () => ({ url: '/api/families/me/invite-codes/rotate', method: 'POST' }),
  invalidatesTags: ['Me'],
}),
leaveFamily: build.mutation<void, void>({
  query: () => ({ url: '/api/families/leave', method: 'POST' }),
  invalidatesTags: ['Me', 'Family', 'FamilyMembers'],
}),
listRelationships: build.query<RelationshipDto[], void>({
  query: () => '/api/families/me/relationships',
  providesTags: ['Relationships'],
}),
addRelationship: build.mutation<RelationshipDto, AddRelationshipRequest>({
  query: (body) => ({ url: '/api/families/me/relationships', method: 'POST', body }),
  invalidatesTags: ['Relationships', 'FamilyMembers'],
}),
deleteRelationship: build.mutation<void, string>({
  query: (id) => ({ url: `/api/families/me/relationships/${id}`, method: 'DELETE' }),
  invalidatesTags: ['Relationships', 'FamilyMembers'],
}),
```

- [ ] **Step 4: Export hooks**

Add to the destructured export block:

```typescript
useJoinFamilyMutation,
useListFamilyMembersQuery,
useRotateInviteCodeMutation,
useLeaveFamilyMutation,
useListRelationshipsQuery,
useAddRelationshipMutation,
useDeleteRelationshipMutation,
```

- [ ] **Step 5: Build to verify**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(family): add RTK Query endpoints for family management"
```

---

## Task 8: Frontend — JoinFamilyPage (Enable Invite Code)

**Files:**
- Modify: `frontend/src/pages/family/JoinFamilyPage.tsx`

- [ ] **Step 1: Update JoinFamilyPage**

Changes needed:
1. Import `useJoinFamilyMutation` and `useSearchParams` from react-router-dom
2. Read `?code=` query param to pre-fill the invite code field
3. Enable the invite code TextBox and Join button
4. Add a `react-hook-form` form for the invite code with validation
5. On submit: call `joinFamily` mutation → redirect to `/`
6. Show error if code is invalid

Replace the invite code section (the disabled TextBox + Button) with:

```tsx
import { useSearchParams } from 'react-router-dom'
import { useJoinFamilyMutation } from '../../shared/api/api'
```

Add state and form setup inside the component:

```tsx
const [searchParams] = useSearchParams()
const codeFromUrl = searchParams.get('code') ?? ''

const [joinFamily, { isLoading: isJoining }] = useJoinFamilyMutation()
const [joinError, setJoinError] = useState<string | null>(null)

const {
  control: joinControl,
  handleSubmit: handleJoinSubmit,
  formState: { errors: joinErrors },
} = useForm<{ inviteCode: string }>({
  defaultValues: { inviteCode: codeFromUrl },
})

const onJoin = handleJoinSubmit(async (values) => {
  setJoinError(null)
  try {
    await joinFamily({ inviteCode: values.inviteCode.trim().toUpperCase() }).unwrap()
  } catch (err) {
    setJoinError(getErrorMessage(err))
  }
})
```

Replace the disabled TextBox + Button with:

```tsx
<form className="join-family__option" onSubmit={onJoin} noValidate>
  <label htmlFor="invite-code">มี invite code แล้ว?</label>
  <Controller
    control={joinControl}
    name="inviteCode"
    rules={{
      required: 'กรุณากรอกรหัสเชิญ',
      pattern: {
        value: /^[A-Za-z0-9]{4}-[A-Za-z0-9]{4}$/,
        message: 'รูปแบบรหัสเชิญไม่ถูกต้อง (XXXX-XXXX)',
      },
    }}
    render={({ field, fieldState }) => (
      <TextBox
        id="invite-code"
        placeholder="XXXX-XXXX"
        value={field.value}
        onChange={(e) => field.onChange(e.value ?? '')}
        disabled={isJoining}
        color={fieldState.error ? ('Error' as never) : undefined}
      />
    )}
  />
  {joinErrors.inviteCode && (
    <p className="field-error">{joinErrors.inviteCode.message}</p>
  )}
  {joinError && <p className="field-error">{joinError}</p>}
  <Button
    type="submit"
    variant={Variant.Filled}
    color={Color.Primary}
    disabled={isJoining}
  >
    {isJoining ? 'กำลังเข้าร่วม…' : 'เข้าร่วม'}
  </Button>
</form>
```

- [ ] **Step 2: Build to verify**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/family/JoinFamilyPage.tsx
git commit -m "feat(family): enable invite code join with QR URL pre-fill"
```

---

## Task 9: Frontend — FamilyPage Hooks

**Files:**
- Create: `frontend/src/pages/family/hooks/useFamilyPage.ts`
- Create: `frontend/src/pages/family/hooks/useAddRelationship.ts`

- [ ] **Step 1: Create useFamilyPage hook**

```typescript
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  useRotateInviteCodeMutation,
  useLeaveFamilyMutation,
  useDeleteRelationshipMutation,
} from '../../../shared/api/api'

function getErrorMessage(err: unknown): string {
  if (typeof err === 'object' && err !== null && 'data' in err) {
    const data = (err as { data?: { detail?: string; title?: string } }).data
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return 'เกิดข้อผิดพลาด กรุณาลองใหม่'
}

export function useFamilyPage() {
  const navigate = useNavigate()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const [rotateCode, { isLoading: isRotating }] = useRotateInviteCodeMutation()
  const [leaveFamily, { isLoading: isLeaving }] = useLeaveFamilyMutation()
  const [deleteRelationship, { isLoading: isDeletingRelationship }] =
    useDeleteRelationshipMutation()

  const handleRotateCode = async () => {
    setErrorMessage(null)
    try {
      await rotateCode().unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleLeaveFamily = async () => {
    setErrorMessage(null)
    try {
      await leaveFamily().unwrap()
      navigate('/join-family', { replace: true })
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  const handleDeleteRelationship = async (id: string) => {
    setErrorMessage(null)
    try {
      await deleteRelationship(id).unwrap()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  }

  return {
    errorMessage,
    isRotating,
    isLeaving,
    isDeletingRelationship,
    handleRotateCode,
    handleLeaveFamily,
    handleDeleteRelationship,
  }
}
```

- [ ] **Step 2: Create useAddRelationship hook**

```typescript
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useAddRelationshipMutation } from '../../../shared/api/api'

export interface AddRelationshipFormValues {
  fromUserId: string
  relationType: string
  toUserId: string
}

function getErrorMessage(err: unknown): string {
  if (typeof err === 'object' && err !== null && 'data' in err) {
    const data = (err as { data?: { detail?: string; title?: string } }).data
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return 'เกิดข้อผิดพลาด กรุณาลองใหม่'
}

export function useAddRelationship() {
  const [isOpen, setIsOpen] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [addRelationship, { isLoading }] = useAddRelationshipMutation()

  const form = useForm<AddRelationshipFormValues>({
    defaultValues: { fromUserId: '', relationType: '', toUserId: '' },
  })

  const open = () => {
    form.reset()
    setErrorMessage(null)
    setIsOpen(true)
  }

  const close = () => {
    setIsOpen(false)
    form.reset()
    setErrorMessage(null)
  }

  const onSubmit = form.handleSubmit(async (values) => {
    setErrorMessage(null)
    try {
      await addRelationship({
        fromUserId: values.fromUserId,
        toUserId: values.toUserId,
        relationType: values.relationType,
      }).unwrap()
      close()
    } catch (err) {
      setErrorMessage(getErrorMessage(err))
    }
  })

  return { isOpen, open, close, form, isLoading, errorMessage, onSubmit }
}
```

- [ ] **Step 3: Build to verify**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/family/hooks/
git commit -m "feat(family): add useFamilyPage and useAddRelationship hooks"
```

---

## Task 10: Frontend — FamilyPage Full Implementation

**Files:**
- Modify: `frontend/src/pages/family/FamilyPage.tsx`

- [ ] **Step 1: Install ej2-react-barcode-generator**

Run: `cd frontend && npm install @syncfusion/ej2-react-barcode-generator`
Expected: added N packages

- [ ] **Step 2: Implement FamilyPage**

Replace the entire file content of `FamilyPage.tsx`. This is the largest task — four sections in one page component with:
- Section 1: Invite code + QR (uses `QRCodeGeneratorComponent`)
- Section 2: Members read-only Grid
- Section 3: Relationships Grid + Add Dialog
- Section 4: Leave family danger zone

```tsx
import { useState } from 'react'
import { Button, Color, Size, Variant } from '@syncfusion/react-buttons'
import { Grid, Column, Columns } from '@syncfusion/react-grid'
import type { ColumnTemplateProps } from '@syncfusion/react-grid'
import { Dialog } from '@syncfusion/react-popups'
import { DropDownList } from '@syncfusion/react-dropdowns'
import { Controller } from 'react-hook-form'
// TODO: migrate to Pure React when available
import { QRCodeGeneratorComponent } from '@syncfusion/ej2-react-barcode-generator'
import {
  useListFamilyMembersQuery,
  useListRelationshipsQuery,
} from '../../shared/api/api'
import type { FamilyMemberDto, RelationshipDto } from '../../shared/api/api'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { useFamilyPage } from './hooks/useFamilyPage'
import { useAddRelationship } from './hooks/useAddRelationship'

const RELATION_TYPE_OPTIONS = [
  { text: 'พ่อ/แม่ (Parent)', value: 'Parent' },
  { text: 'ลูก (Child)', value: 'Child' },
  { text: 'คู่สมรส (Spouse)', value: 'Spouse' },
  { text: 'พี่น้อง (Sibling)', value: 'Sibling' },
  { text: 'อื่นๆ (Other)', value: 'Other' },
]

const RELATION_LABEL_MAP: Record<string, string> = {
  Parent: 'พ่อ/แม่',
  Child: 'ลูก',
  Spouse: 'คู่สมรส',
  Sibling: 'พี่น้อง',
  Other: 'อื่นๆ',
}

const MENU_NEST_LOGO_SVG =
  'data:image/svg+xml;base64,' +
  btoa(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40">' +
      '<circle cx="20" cy="20" r="19" fill="#F57C00"/>' +
      '<circle cx="20" cy="20" r="17" fill="#fff"/>' +
      '<circle cx="20" cy="20" r="15" fill="#F57C00"/>' +
      '<text x="20" y="27" text-anchor="middle" font-size="16" fill="white" font-weight="bold">M</text>' +
      '</svg>',
  )

export function FamilyPage() {
  const { familyName, familyInviteCode } = useCurrentUser()
  const { data: members } = useListFamilyMembersQuery()
  const { data: relationships } = useListRelationshipsQuery()

  const {
    errorMessage,
    isRotating,
    isLeaving,
    handleRotateCode,
    handleLeaveFamily,
    handleDeleteRelationship,
  } = useFamilyPage()

  const addRel = useAddRelationship()

  const [showLeaveConfirm, setShowLeaveConfirm] = useState(false)
  const [showRotateConfirm, setShowRotateConfirm] = useState(false)

  const inviteUrl = familyInviteCode
    ? `${window.location.origin}/join?code=${familyInviteCode}`
    : ''

  /* ---------- Column templates ---------- */

  const MemberNameTemplate = ({ data: m }: ColumnTemplateProps<FamilyMemberDto>) => {
    const initial = m.displayName.charAt(0)
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <div
          style={{
            width: 32, height: 32, borderRadius: '50%',
            background: 'linear-gradient(135deg, #F57C00, #E65100)',
            color: '#fff', display: 'flex', alignItems: 'center',
            justifyContent: 'center', fontWeight: 600, fontSize: 14,
          }}
        >
          {initial}
        </div>
        <span style={{ fontWeight: 500 }}>{m.displayName}</span>
      </div>
    )
  }

  const MemberRelBadgeTemplate = ({ data: m }: ColumnTemplateProps<FamilyMemberDto>) => (
    <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
      {m.isCreator && (
        <span className="badge badge--creator">ผู้สร้าง</span>
      )}
      {m.relationships.map((r) => (
        <span key={r.relationshipId} className="badge badge--relation">
          {r.label}
        </span>
      ))}
    </div>
  )

  const MemberJoinedTemplate = ({ data: m }: ColumnTemplateProps<FamilyMemberDto>) => (
    <span style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>
      {new Date(m.joinedAt).toLocaleDateString('th-TH', { day: 'numeric', month: 'short' })}
    </span>
  )

  const RelTypeTemplate = ({ data: r }: ColumnTemplateProps<RelationshipDto>) => (
    <span className="badge badge--relation">
      {RELATION_LABEL_MAP[r.relationType] ?? r.relationType}
    </span>
  )

  // Dropdown data for member picker
  const memberOptions = members?.map((m) => ({
    text: m.displayName,
    value: m.userId,
  })) ?? []

  return (
    <section className="page page--family">
      <header className="page__header">
        <h1>{familyName ?? 'Family'}</h1>
      </header>

      {errorMessage && <div className="error-banner">{errorMessage}</div>}

      {/* ---- Section 1: Invite Code + QR ---- */}
      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ fontSize: 16, marginBottom: 4 }}>รหัสเชิญ</h2>
        <p style={{ fontSize: 13, color: 'var(--color-text-muted)', marginBottom: 16 }}>
          ส่งรหัสหรือ QR code นี้ให้สมาชิกครอบครัว — scan แล้วเข้าร่วมได้เลย
        </p>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 24, flexWrap: 'wrap' }}>
          <div>
            <div style={{ fontFamily: 'monospace', fontSize: 28, fontWeight: 700, letterSpacing: 3, color: 'var(--color-primary-dark)', marginBottom: 12 }}>
              {familyInviteCode ?? '----'}
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <Button
                type="button"
                variant={Variant.Outlined}
                color={Color.Secondary}
                onClick={() => navigator.clipboard?.writeText(familyInviteCode ?? '')}
              >
                คัดลอก
              </Button>
              <Button
                type="button"
                variant={Variant.Outlined}
                color={Color.Secondary}
                onClick={() => setShowRotateConfirm(true)}
                disabled={isRotating}
              >
                สร้างรหัสใหม่
              </Button>
            </div>
          </div>
          {familyInviteCode && (
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6 }}>
              <div style={{ border: '2px solid var(--color-primary)', borderRadius: 12, padding: 12, background: 'var(--color-primary-light)' }}>
                <QRCodeGeneratorComponent
                  value={inviteUrl}
                  width="120px"
                  height="120px"
                  foreColor="#E65100"
                  backgroundColor="transparent"
                  errorCorrectionLevel={30}
                  logo={{ imageSource: MENU_NEST_LOGO_SVG, width: 30, height: 30 }}
                  displayText={{ visibility: false }}
                  mode="SVG"
                  margin={{ left: 2, right: 2, top: 2, bottom: 2 }}
                />
              </div>
              <span style={{ fontSize: 11, color: 'var(--color-primary-dark)', fontWeight: 600 }}>
                Scan เพื่อเข้าร่วม
              </span>
            </div>
          )}
        </div>
      </div>

      {/* ---- Section 2: Members Grid ---- */}
      <div className="card" style={{ marginBottom: 16 }}>
        <h2 style={{ fontSize: 16, marginBottom: 12 }}>
          สมาชิก ({members?.length ?? 0} คน)
        </h2>
        {members && (
          <Grid dataSource={members as FamilyMemberDto[]} height="auto">
            <Columns>
              <Column field="displayName" headerText="ชื่อ" template={MemberNameTemplate} />
              <Column field="email" headerText="อีเมล" />
              <Column headerText="ความสัมพันธ์" template={MemberRelBadgeTemplate} />
              <Column headerText="เข้าร่วม" width={100} template={MemberJoinedTemplate} />
            </Columns>
          </Grid>
        )}
      </div>

      {/* ---- Section 3: Relationships Grid ---- */}
      <div className="card" style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
          <h2 style={{ fontSize: 16 }}>ความสัมพันธ์</h2>
          <Button
            type="button"
            variant={Variant.Outlined}
            color={Color.Primary}
            size={Size.Small}
            onClick={addRel.open}
          >
            + เพิ่ม
          </Button>
        </div>
        <p style={{ fontSize: 13, color: 'var(--color-text-muted)', marginBottom: 12 }}>
          กำหนดความสัมพันธ์ระหว่างสมาชิก — badge จะแสดงในตารางด้านบนอัตโนมัติ
        </p>
        {relationships && relationships.length > 0 ? (
          <Grid dataSource={relationships as RelationshipDto[]} height="auto">
            <Columns>
              <Column field="fromUserName" headerText="จาก" />
              <Column field="relationType" headerText="ความสัมพันธ์" template={RelTypeTemplate} />
              <Column field="toUserName" headerText="ถึง" />
              <Column
                headerText=""
                width={60}
                template={({ data: r }: ColumnTemplateProps<RelationshipDto>) => (
                  <Button
                    type="button"
                    size={Size.Small}
                    variant={Variant.Outlined}
                    color={Color.Secondary}
                    onClick={() => handleDeleteRelationship(r.id)}
                  >
                    🗑
                  </Button>
                )}
              />
            </Columns>
          </Grid>
        ) : (
          <p style={{ color: 'var(--color-text-muted)', textAlign: 'center', padding: 24 }}>
            ยังไม่มีความสัมพันธ์ — กด "+ เพิ่ม" เพื่อเริ่มต้น
          </p>
        )}
      </div>

      {/* ---- Section 4: Danger Zone ---- */}
      <div className="card" style={{ marginBottom: 16, borderColor: '#FECDD3', background: '#FFF5F5' }}>
        <h2 style={{ fontSize: 16, color: 'var(--color-danger)', marginBottom: 8 }}>
          Danger Zone
        </h2>
        <p style={{ fontSize: 13, color: 'var(--color-text-muted)', marginBottom: 12 }}>
          ออกจากครอบครัวนี้ — คุณจะไม่เห็น recipe, stock, meal plan, และ shopping list ของครอบครัวนี้อีกต่อไป
        </p>
        <Button
          type="button"
          variant={Variant.Outlined}
          onClick={() => setShowLeaveConfirm(true)}
          disabled={isLeaving}
          style={{ borderColor: 'var(--color-danger)', color: 'var(--color-danger)' }}
        >
          ออกจากครอบครัว
        </Button>
      </div>

      {/* ---- Add Relationship Dialog ---- */}
      {addRel.isOpen && (
        <Dialog
          isModal
          visible={addRel.isOpen}
          header="เพิ่มความสัมพันธ์"
          close={addRel.close}
          width="440px"
        >
          <form onSubmit={addRel.onSubmit} noValidate>
            <div style={{ padding: '16px 0', display: 'flex', flexDirection: 'column', gap: 16 }}>
              <div>
                <label style={{ display: 'block', fontSize: 13, fontWeight: 500, marginBottom: 4 }}>
                  จากสมาชิก <span style={{ color: 'var(--color-danger)' }}>*</span>
                </label>
                <Controller
                  control={addRel.form.control}
                  name="fromUserId"
                  rules={{ required: 'กรุณาเลือกสมาชิก' }}
                  render={({ field }) => (
                    <DropDownList
                      dataSource={memberOptions}
                      fields={{ text: 'text', value: 'value' }}
                      value={field.value}
                      onChange={(e) => field.onChange(e.value ?? '')}
                      placeholder="— เลือกสมาชิก —"
                    />
                  )}
                />
                {addRel.form.formState.errors.fromUserId && (
                  <p className="field-error">{addRel.form.formState.errors.fromUserId.message}</p>
                )}
              </div>

              <div>
                <label style={{ display: 'block', fontSize: 13, fontWeight: 500, marginBottom: 4 }}>
                  เป็น <span style={{ color: 'var(--color-danger)' }}>*</span>
                </label>
                <Controller
                  control={addRel.form.control}
                  name="relationType"
                  rules={{ required: 'กรุณาเลือกความสัมพันธ์' }}
                  render={({ field }) => (
                    <DropDownList
                      dataSource={RELATION_TYPE_OPTIONS}
                      fields={{ text: 'text', value: 'value' }}
                      value={field.value}
                      onChange={(e) => field.onChange(e.value ?? '')}
                      placeholder="— เลือกความสัมพันธ์ —"
                    />
                  )}
                />
                {addRel.form.formState.errors.relationType && (
                  <p className="field-error">{addRel.form.formState.errors.relationType.message}</p>
                )}
              </div>

              <div>
                <label style={{ display: 'block', fontSize: 13, fontWeight: 500, marginBottom: 4 }}>
                  ของสมาชิก <span style={{ color: 'var(--color-danger)' }}>*</span>
                </label>
                <Controller
                  control={addRel.form.control}
                  name="toUserId"
                  rules={{ required: 'กรุณาเลือกสมาชิก' }}
                  render={({ field }) => (
                    <DropDownList
                      dataSource={memberOptions}
                      fields={{ text: 'text', value: 'value' }}
                      value={field.value}
                      onChange={(e) => field.onChange(e.value ?? '')}
                      placeholder="— เลือกสมาชิก —"
                    />
                  )}
                />
                {addRel.form.formState.errors.toUserId && (
                  <p className="field-error">{addRel.form.formState.errors.toUserId.message}</p>
                )}
              </div>

              {addRel.errorMessage && (
                <div className="error-banner">{addRel.errorMessage}</div>
              )}
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, paddingTop: 8, borderTop: '1px solid var(--color-border)' }}>
              <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={addRel.close}>
                ยกเลิก
              </Button>
              <Button type="submit" variant={Variant.Filled} color={Color.Primary} disabled={addRel.isLoading}>
                {addRel.isLoading ? 'กำลังบันทึก…' : 'บันทึก'}
              </Button>
            </div>
          </form>
        </Dialog>
      )}

      {/* ---- Rotate Confirm Dialog ---- */}
      {showRotateConfirm && (
        <Dialog
          isModal
          visible={showRotateConfirm}
          header="สร้างรหัสเชิญใหม่"
          close={() => setShowRotateConfirm(false)}
          width="380px"
        >
          <p style={{ margin: '16px 0' }}>
            รหัสเก่าจะใช้ไม่ได้อีกต่อไป — ต้องการสร้างรหัสใหม่?
          </p>
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={() => setShowRotateConfirm(false)}>
              ยกเลิก
            </Button>
            <Button
              type="button"
              variant={Variant.Filled}
              color={Color.Primary}
              disabled={isRotating}
              onClick={async () => {
                await handleRotateCode()
                setShowRotateConfirm(false)
              }}
            >
              {isRotating ? 'กำลังสร้าง…' : 'สร้างรหัสใหม่'}
            </Button>
          </div>
        </Dialog>
      )}

      {/* ---- Leave Confirm Dialog ---- */}
      {showLeaveConfirm && (
        <Dialog
          isModal
          visible={showLeaveConfirm}
          header="ออกจากครอบครัว"
          close={() => setShowLeaveConfirm(false)}
          width="380px"
        >
          <p style={{ margin: '16px 0' }}>
            คุณจะไม่เห็น recipe, stock, meal plan, และ shopping list ของครอบครัวนี้อีกต่อไป — ต้องการออก?
          </p>
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
            <Button type="button" variant={Variant.Outlined} color={Color.Secondary} onClick={() => setShowLeaveConfirm(false)}>
              ยกเลิก
            </Button>
            <Button
              type="button"
              variant={Variant.Filled}
              onClick={handleLeaveFamily}
              disabled={isLeaving}
              style={{ background: 'var(--color-danger)', borderColor: 'var(--color-danger)' }}
            >
              {isLeaving ? 'กำลังออก…' : 'ออกจากครอบครัว'}
            </Button>
          </div>
        </Dialog>
      )}
    </section>
  )
}
```

- [ ] **Step 3: Update barrel exports**

In `frontend/src/pages/family/index.ts`, ensure both pages are exported:

```typescript
export { FamilyPage } from './FamilyPage'
export { JoinFamilyPage } from './JoinFamilyPage'
```

- [ ] **Step 4: Build to verify**

Run: `cd frontend && npx tsc --noEmit`
Expected: No errors

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/family/ frontend/package.json frontend/package-lock.json
git commit -m "feat(family): implement FamilyPage with invite QR, members grid, relationships, leave"
```

---

## Task 11: Verification — Full Build + Manual Test

- [ ] **Step 1: Build backend**

Run: `dotnet build backend/`
Expected: Build succeeded, 0 warnings related to new files

- [ ] **Step 2: Build frontend**

Run: `cd frontend && npx tsc --noEmit`
Expected: No type errors

- [ ] **Step 3: Start dev servers and test**

1. Start backend: `dotnet run --project backend/src/MenuNest.WebApi`
2. Start frontend: `cd frontend && npm run dev`
3. Open `http://localhost:5173`

**Manual test checklist:**
- [ ] Navigate to `/family` — page loads with invite code, QR code, members grid
- [ ] Click "คัดลอก" — invite code copied to clipboard
- [ ] Click "สร้างรหัสใหม่" → confirm dialog → new code appears, QR updates
- [ ] Members grid shows current user with "ผู้สร้าง" badge
- [ ] Click "+ เพิ่ม" on Relationships → dialog opens with 3 dropdowns
- [ ] Fill form and submit → new relationship appears in grid + badge in members
- [ ] Click 🗑 on a relationship → relationship removed
- [ ] Click "ออกจากครอบครัว" → confirm → redirected to `/join-family`
- [ ] On JoinFamilyPage, enter an invite code → click "เข้าร่วม" → redirected to `/`
- [ ] Open URL `/join?code=XXXX-XXXX` → invite code pre-filled

- [ ] **Step 4: Final commit (if any fixes needed)**

```bash
git add -A
git commit -m "fix(family): address issues found during manual testing"
```

---

## Summary

| Task | Description | Files |
|------|------------|-------|
| 1 | Backend DTOs | 2 new |
| 2 | JoinFamily handler | 3 new |
| 3 | ListFamilyMembers handler | 2 new |
| 4 | RotateInviteCode + LeaveFamily | 4 new |
| 5 | Relationship handlers (Add/Delete/List) | 7 new |
| 6 | FamiliesController endpoints | 1 modified |
| 7 | RTK Query endpoints + DTOs | 1 modified |
| 8 | JoinFamilyPage enable invite code | 1 modified |
| 9 | FamilyPage hooks | 2 new |
| 10 | FamilyPage implementation | 2 modified |
| 11 | Verification | — |

**Total: 20 new files, 5 modified files, 11 tasks**
