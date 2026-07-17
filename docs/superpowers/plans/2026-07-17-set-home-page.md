# Set Home page (issue #39) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each User choose which existing app page `/` opens to (their "Home page"), persisted server-side.

**Architecture:** New 1:1 `UserSettings` entity (`HomePath` column) read via `GET /api/me` and written via `PUT /api/me/settings`; a new auth-only `/settings` page sets it with a family-aware auto-saving dropdown reached from the account menu; a `HomeRedirect` element resolves `/` against a validated allowlist, defaulting to `/budget`.

**Tech Stack:** Backend — .NET, EF Core (SQL Server), the source-generated **Mediator** library (martinothamar/Mediator — **NOT** MediatR), FluentValidation, xUnit + Moq + FluentAssertions. Frontend — React 19 + react-router-dom v7 (data router), Redux Toolkit Query (one `createApi` slice), Syncfusion Pure-React (`@syncfusion/react-dropdowns`), vitest (node env).

## Global Constraints

- **Mediator, not MediatR:** reads implement `IQuery<T>`/`IQueryHandler<TQuery,T>`, writes `ICommand<T>`/`ICommandHandler<TCommand,T>`; handler method is `public async ValueTask<T> Handle(msg, CancellationToken ct)`; controllers inject `IMediator` and call `await _mediator.Send(msg, ct)`.
- **New `DbSet<UserSettings>` MUST be added to `IApplicationDbContext` AND all three implementers (`AppDbContext`, `SqliteAppDbContext`, `InMemoryAppDbContext`) in the SAME commit as the entity + EF config**, or pre-commit (full backend `dotnet build`+`dotnet test` Release) fails `CS0535` / EF model validation.
- **Migrations are applied to prod MANUALLY** (app/CD do not migrate) — Task 8. Preview with `dotnet ef migrations script --idempotent`.
- Backend tests: **xUnit + Moq + FluentAssertions** (NOT NSubstitute); relational handler tests use `SqliteAppDbContext`.
- Frontend has **no component/visual test harness** (vitest `environment: 'node'`, no jsdom) — all logic lives in the pure lib `frontend/src/pages/settings/homeOptions.ts` with `*.test.ts`; UI is verified interactively.
- **Icons:** new UI uses **inline hand-authored `<svg>`** (viewBox `0 0 24 24`, `stroke="currentColor"`), matching the existing `StopEditorDialog.tsx` precedent — **never emoji**. (`@syncfusion/react-icons` is documented but is **not** a declared dependency and is used by zero files; adopting it is out of scope to avoid the version-skew license-banner risk.)
- **Commits reference #39**; stage narrowly with explicit paths (**never** `git add -A`/`.`); the git remote is named **`main`** (not `origin`) — pushing is out of scope for this plan.
- Endpoint is **`PUT /api/me/settings`** (extensible), not `PUT /api/me/home-page`.

## File Structure

**Backend (create):**
- `backend/src/MenuNest.Domain/Entities/UserSettings.cs` — the entity.
- `backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserSettingsConfiguration.cs` — EF mapping.
- `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsCommand.cs`, `…Validator.cs`, `…Handler.cs`, `…/UserSettingsDto.cs` — the write use case.
- `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<ts>_AddUserSettings.cs` (+ `.Designer.cs` + snapshot) — EF-generated in Task 1.
- `backend/tests/MenuNest.Application.UnitTests/…` — handler/mapping tests.

**Backend (modify):**
- `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs` — add `DbSet<UserSettings>`.
- `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`, `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs`, `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs` — add the `DbSet` line.
- `backend/src/MenuNest.Application/UseCases/Me/MeDto.cs` — add `HomePath`.
- `backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs` — read + map `HomePath`.
- `backend/src/MenuNest.WebApi/Controllers/MeController.cs` — add `PUT settings`.

**Frontend (create):**
- `frontend/src/pages/settings/homeOptions.ts` + `homeOptions.test.ts` — pure lib.
- `frontend/src/pages/settings/SettingsPage.tsx`, `SettingsPage.css`, `index.ts`.
- `frontend/src/shared/components/HomeRedirect.tsx`.

**Frontend (modify):**
- `frontend/src/shared/api/api.ts` — `MeDto.homePath` + `updateUserSettings` mutation + hook.
- `frontend/src/shared/hooks/useCurrentUser.ts` — return `homePath`.
- `frontend/src/router.tsx` — `/` → `<HomeRedirect/>`; add `/settings`.
- `frontend/src/shared/components/NavBar.tsx` — account-menu + drawer "Settings" entry.

---

### Task 1: `UserSettings` entity + EF config + DbSet (all 3 contexts) + migration

**Files:**
- Create: `backend/src/MenuNest.Domain/Entities/UserSettings.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserSettingsConfiguration.cs`
- Modify: `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs`
- Modify: `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`
- Create (EF-generated): `backend/src/MenuNest.Infrastructure/Persistence/Migrations/<ts>_AddUserSettings.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Persistence/UserSettingsPersistenceTests.cs`

**Interfaces:**
- Produces: `UserSettings` entity (`Guid Id`, `Guid UserId`, `string? HomePath`, static `Create(Guid userId)`, `void SetHomePath(string?)`); `IApplicationDbContext.UserSettings` (`DbSet<UserSettings>`).

- [ ] **Step 1: Write the failing persistence test**

Create `backend/tests/MenuNest.Application.UnitTests/Persistence/UserSettingsPersistenceTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MenuNest.Application.UnitTests.Persistence;

public sealed class UserSettingsPersistenceTests
{
    private static SqliteAppDbContext NewContext(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new SqliteAppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task HomePath_round_trips_and_UserId_is_unique()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);

        var user = User.CreateFromExternalLogin("ext-1", "a@b.com", "A", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var settings = UserSettings.Create(user.Id);
        settings.SetHomePath("/pomodoro");
        ctx.UserSettings.Add(settings);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.UserSettings.SingleAsync(s => s.UserId == user.Id);
        loaded.HomePath.Should().Be("/pomodoro");

        // Second row for the same user must violate the unique index.
        ctx.UserSettings.Add(UserSettings.Create(user.Id));
        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (does not compile — `UserSettings` missing)**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~UserSettingsPersistenceTests`
Expected: FAIL — build error, `UserSettings` / `ctx.UserSettings` do not exist.

- [ ] **Step 3: Create the entity**

Create `backend/src/MenuNest.Domain/Entities/UserSettings.cs`:

```csharp
using MenuNest.Domain.Common;

namespace MenuNest.Domain.Entities;

/// <summary>
/// Per-user preferences (1:1 with <see cref="User"/>). Created lazily on
/// first write. Currently holds only the user's chosen Home page route.
/// </summary>
public sealed class UserSettings : Entity
{
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;

    /// <summary>The route "/" resolves to, e.g. "/pomodoro". Null = unset.</summary>
    public string? HomePath { get; private set; }

    // EF Core
    private UserSettings() { }

    public static UserSettings Create(Guid userId)
    {
        return new UserSettings { UserId = userId };
    }

    public void SetHomePath(string? homePath)
    {
        HomePath = string.IsNullOrWhiteSpace(homePath) ? null : homePath.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Create the EF configuration**

Create `backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserSettingsConfiguration.cs`:

```csharp
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("UserSettings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.HomePath).HasMaxLength(100);

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt);

        // 1:1 with User; the FK carries a unique index (enforces one row per user).
        builder.HasOne(s => s.User)
            .WithOne()
            .HasForeignKey<UserSettings>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 5: Add the DbSet to the interface and all three implementers**

In `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs`, add after `DbSet<User> Users { get; }`:

```csharp
    DbSet<UserSettings> UserSettings { get; }
```

In `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`, add beside the other DbSet members:

```csharp
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
```

In `backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs`, add the same line beside its DbSet members:

```csharp
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
```

In `backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs`, add the same line beside its DbSet members (no `OnModelCreating` change — a plain scalar 1:1 needs no manual mirroring):

```csharp
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~UserSettingsPersistenceTests`
Expected: PASS (2 assertions + the unique-violation throw).

- [ ] **Step 7: Generate the migration**

Run (from `backend/`):

```bash
dotnet ef migrations add AddUserSettings \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Expected: creates `src/MenuNest.Infrastructure/Persistence/Migrations/<ts>_AddUserSettings.cs` (a `CreateTable("UserSettings")` with `Id` PK, `UserId` + unique index, `HomePath nvarchar(100)`, `CreatedAt`, `UpdatedAt`, FK to `Users` on delete cascade), its `.Designer.cs`, and an updated model snapshot. Do NOT hand-edit these.

- [ ] **Step 8: Build to confirm the whole solution still compiles**

Run: `dotnet build backend/MenuNest.sln -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit (entity + config + all 3 DbSets + migration together)**

```bash
git add backend/src/MenuNest.Domain/Entities/UserSettings.cs \
  backend/src/MenuNest.Infrastructure/Persistence/Configurations/UserSettingsConfiguration.cs \
  backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs \
  backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs \
  backend/tests/MenuNest.Application.UnitTests/Support/SqliteAppDbContext.cs \
  backend/tests/MenuNest.Application.UnitTests/Support/InMemoryAppDbContext.cs \
  backend/tests/MenuNest.Application.UnitTests/Persistence/UserSettingsPersistenceTests.cs \
  backend/src/MenuNest.Infrastructure/Persistence/Migrations/
git commit -m "feat(settings): add UserSettings entity + migration for Home page (#39)"
```

---

### Task 2: Surface `HomePath` on `GET /api/me`

**Files:**
- Modify: `backend/src/MenuNest.Application/UseCases/Me/MeDto.cs`
- Modify: `backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Me/GetMeHandlerTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext.UserSettings` (Task 1).
- Produces: `MeDto.HomePath` (`string?`) — the frontend reads it (Task 5).

- [ ] **Step 1: Write the failing handler test**

Create `backend/tests/MenuNest.Application.UnitTests/Me/GetMeHandlerTests.cs`:

```csharp
using FluentAssertions;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.UseCases.Me.GetMe;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Me;

public sealed class GetMeHandlerTests
{
    private static SqliteAppDbContext NewContext(SqliteConnection conn)
    {
        var ctx = new SqliteAppDbContext(
            new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task Returns_HomePath_when_a_UserSettings_row_exists()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);

        var user = User.CreateFromExternalLogin("ext-1", "a@b.com", "A", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        var settings = UserSettings.Create(user.Id);
        settings.SetHomePath("/trips");
        ctx.UserSettings.Add(settings);
        await ctx.SaveChangesAsync();

        var provisioner = new Mock<IUserProvisioner>();
        provisioner.Setup(p => p.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = new GetMeHandler(provisioner.Object, ctx);
        var me = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        me.HomePath.Should().Be("/trips");
    }

    [Fact]
    public async Task Returns_null_HomePath_when_no_UserSettings_row()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);

        var user = User.CreateFromExternalLogin("ext-2", "b@b.com", "B", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var provisioner = new Mock<IUserProvisioner>();
        provisioner.Setup(p => p.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = new GetMeHandler(provisioner.Object, ctx);
        var me = await handler.Handle(new GetMeQuery(), CancellationToken.None);

        me.HomePath.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GetMeHandlerTests`
Expected: FAIL — `GetMeHandler` has no 2-arg constructor and `MeDto.HomePath` does not exist.

- [ ] **Step 3: Add `HomePath` to `MeDto`**

In `backend/src/MenuNest.Application/UseCases/Me/MeDto.cs`, add a trailing member to the record:

```csharp
public sealed record MeDto(
    Guid UserId,
    string Email,
    string DisplayName,
    Guid? FamilyId,
    string? FamilyName,
    string? FamilyInviteCode,
    string AuthProvider,
    string? HomePath);
```

- [ ] **Step 4: Read + map `HomePath` in `GetMeHandler`**

Replace `backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs` with:

```csharp
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Me.GetMe;

/// <summary>
/// Returns the current caller's profile, auto-provisioning a
/// <c>User</c> row on first sign-in via <see cref="IUserProvisioner"/>.
/// </summary>
public sealed class GetMeHandler : IQueryHandler<GetMeQuery, MeDto>
{
    private readonly IUserProvisioner _userProvisioner;
    private readonly IApplicationDbContext _db;

    public GetMeHandler(IUserProvisioner userProvisioner, IApplicationDbContext db)
    {
        _userProvisioner = userProvisioner;
        _db = db;
    }

    public async ValueTask<MeDto> Handle(GetMeQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var settings = await _db.UserSettings
            .FirstOrDefaultAsync(s => s.UserId == user.Id, ct);

        return new MeDto(
            UserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            FamilyId: user.FamilyId,
            FamilyName: user.Family?.Name,
            FamilyInviteCode: user.Family?.InviteCode.Value,
            AuthProvider: user.AuthProvider.ToString(),
            HomePath: settings?.HomePath);
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~GetMeHandlerTests`
Expected: PASS (both facts).

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Me/MeDto.cs \
  backend/src/MenuNest.Application/UseCases/Me/GetMe/GetMeHandler.cs \
  backend/tests/MenuNest.Application.UnitTests/Me/GetMeHandlerTests.cs
git commit -m "feat(settings): expose HomePath on GET /api/me (#39)"
```

---

### Task 3: Write endpoint — `PUT /api/me/settings`

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Me/UserSettingsDto.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsValidator.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsHandler.cs`
- Modify: `backend/src/MenuNest.WebApi/Controllers/MeController.cs`
- Test: `backend/tests/MenuNest.Application.UnitTests/Me/UpdateUserSettingsHandlerTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext.UserSettings`, `UserSettings.Create`/`SetHomePath`, `IUserProvisioner`.
- Produces: `UpdateUserSettingsCommand(string? HomePath) : ICommand<UserSettingsDto>`; `UserSettingsDto(string? HomePath)`; `PUT api/me/settings`.

- [ ] **Step 1: Write the failing handler test**

Create `backend/tests/MenuNest.Application.UnitTests/Me/UpdateUserSettingsHandlerTests.cs`:

```csharp
using FluentAssertions;
using FluentValidation;
using MenuNest.Application.UnitTests.Support;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Me.UpdateUserSettings;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MenuNest.Application.UnitTests.Me;

public sealed class UpdateUserSettingsHandlerTests
{
    private static SqliteAppDbContext NewContext(SqliteConnection conn)
    {
        var ctx = new SqliteAppDbContext(
            new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(conn).Options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static UpdateUserSettingsHandler NewHandler(SqliteAppDbContext ctx, User user)
    {
        var provisioner = new Mock<IUserProvisioner>();
        provisioner.Setup(p => p.GetOrProvisionCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return new UpdateUserSettingsHandler(ctx, provisioner.Object, new UpdateUserSettingsValidator());
    }

    [Fact]
    public async Task Creates_the_row_on_first_write_then_updates_it()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        var user = User.CreateFromExternalLogin("ext-1", "a@b.com", "A", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var handler = NewHandler(ctx, user);

        var first = await handler.Handle(new UpdateUserSettingsCommand("/pomodoro"), CancellationToken.None);
        first.HomePath.Should().Be("/pomodoro");
        (await ctx.UserSettings.CountAsync()).Should().Be(1);

        var second = await handler.Handle(new UpdateUserSettingsCommand("/trips"), CancellationToken.None);
        second.HomePath.Should().Be("/trips");
        (await ctx.UserSettings.CountAsync()).Should().Be(1); // still one row (updated, not inserted)
    }

    [Fact]
    public async Task Null_clears_the_HomePath()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        var user = User.CreateFromExternalLogin("ext-2", "b@b.com", "B", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var handler = NewHandler(ctx, user);

        await handler.Handle(new UpdateUserSettingsCommand("/trips"), CancellationToken.None);
        var cleared = await handler.Handle(new UpdateUserSettingsCommand(null), CancellationToken.None);

        cleared.HomePath.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_a_HomePath_longer_than_100_chars()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var ctx = NewContext(conn);
        var user = User.CreateFromExternalLogin("ext-3", "c@b.com", "C", AuthProvider.Microsoft);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var handler = NewHandler(ctx, user);

        var act = async () => await handler.Handle(
            new UpdateUserSettingsCommand(new string('x', 101)), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~UpdateUserSettingsHandlerTests`
Expected: FAIL — the `UpdateUserSettings*` types do not exist.

- [ ] **Step 3: Create the DTO**

Create `backend/src/MenuNest.Application/UseCases/Me/UserSettingsDto.cs`:

```csharp
namespace MenuNest.Application.UseCases.Me;

/// <summary>Response payload for <c>PUT /api/me/settings</c>.</summary>
public sealed record UserSettingsDto(string? HomePath);
```

- [ ] **Step 4: Create the command**

Create `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsCommand.cs`:

```csharp
using Mediator;
using MenuNest.Application.UseCases.Me;

namespace MenuNest.Application.UseCases.Me.UpdateUserSettings;

public sealed record UpdateUserSettingsCommand(string? HomePath) : ICommand<UserSettingsDto>;
```

- [ ] **Step 5: Create the validator**

Create `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsValidator.cs`:

```csharp
using FluentValidation;

namespace MenuNest.Application.UseCases.Me.UpdateUserSettings;

public sealed class UpdateUserSettingsValidator : AbstractValidator<UpdateUserSettingsCommand>
{
    public UpdateUserSettingsValidator()
    {
        // HomePath is optional (null clears it). Route validity is enforced
        // client-side against the home-eligible allowlist (ADR-084); the
        // server only bounds the length to match the column.
        RuleFor(x => x.HomePath)
            .MaximumLength(100).WithMessage("HomePath must be 100 characters or less.");
    }
}
```

- [ ] **Step 6: Create the handler**

Create `backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/UpdateUserSettingsHandler.cs`:

```csharp
using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Me;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Me.UpdateUserSettings;

/// <summary>
/// Sets the current caller's Home page. Creates the caller's
/// <c>UserSettings</c> row lazily on first write.
/// </summary>
public sealed class UpdateUserSettingsHandler : ICommandHandler<UpdateUserSettingsCommand, UserSettingsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<UpdateUserSettingsCommand> _validator;

    public UpdateUserSettingsHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<UpdateUserSettingsCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<UserSettingsDto> Handle(UpdateUserSettingsCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == user.Id, ct);
        if (settings is null)
        {
            settings = UserSettings.Create(user.Id);
            _db.UserSettings.Add(settings);
        }

        settings.SetHomePath(command.HomePath);
        await _db.SaveChangesAsync(ct);

        return new UserSettingsDto(settings.HomePath);
    }
}
```

- [ ] **Step 7: Add the controller action**

Replace `backend/src/MenuNest.WebApi/Controllers/MeController.cs` with:

```csharp
using Mediator;
using MenuNest.Application.UseCases.Me;
using MenuNest.Application.UseCases.Me.GetMe;
using MenuNest.Application.UseCases.Me.UpdateUserSettings;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MeController : ControllerBase
{
    private readonly IMediator _mediator;

    public MeController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Returns the current caller's profile.</summary>
    [HttpGet]
    public async Task<ActionResult<MeDto>> GetMe(CancellationToken ct)
    {
        var me = await _mediator.Send(new GetMeQuery(), ct);
        return Ok(me);
    }

    /// <summary>Updates the current caller's settings (Home page).</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<UserSettingsDto>> UpdateSettings(
        [FromBody] UpdateUserSettingsCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}
```

- [ ] **Step 8: Run to verify it passes**

Run: `dotnet test backend/tests/MenuNest.Application.UnitTests --filter FullyQualifiedName~UpdateUserSettingsHandlerTests`
Expected: PASS (3 facts). (Handler + validator are auto-registered — source-generated Mediator scans the Application assembly and FluentValidation registers `AbstractValidator<>` implementations by assembly scan; no manual DI wiring.)

- [ ] **Step 9: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Me/UserSettingsDto.cs \
  backend/src/MenuNest.Application/UseCases/Me/UpdateUserSettings/ \
  backend/src/MenuNest.WebApi/Controllers/MeController.cs \
  backend/tests/MenuNest.Application.UnitTests/Me/UpdateUserSettingsHandlerTests.cs
git commit -m "feat(settings): add PUT /api/me/settings to set Home page (#39)"
```

---

### Task 4: Frontend pure lib — `homeOptions.ts` + tests

**Files:**
- Create: `frontend/src/pages/settings/homeOptions.ts`
- Test: `frontend/src/pages/settings/homeOptions.test.ts`

**Interfaces:**
- Produces: `HOME_OPTIONS`, `homeOptions(hasFamily: boolean): HomeOption[]`, `resolveHomePath(homePath: string | null | undefined): string`, `type HomeOption = { path: string; label: string; requiresFamily: boolean }`.
- Note: `resolveHomePath` takes only `homePath` — family gating is handled by the route guards (ADR-084), so it is a pure allowlist lookup (deviates from the spec's listed `(homePath, hasFamily)` signature to avoid an unused parameter).

- [ ] **Step 1: Write the failing test**

Create `frontend/src/pages/settings/homeOptions.test.ts`:

```typescript
import { describe, it, expect } from 'vitest'
import { HOME_OPTIONS, homeOptions, resolveHomePath } from './homeOptions'

describe('homeOptions', () => {
  it('returns all pages for a user with a family', () => {
    expect(homeOptions(true)).toHaveLength(HOME_OPTIONS.length)
  })

  it('hides family-gated pages for a user with no family', () => {
    const opts = homeOptions(false)
    expect(opts.every((o) => !o.requiresFamily)).toBe(true)
    expect(opts.map((o) => o.path)).toEqual(['/health', '/pomodoro', '/trips'])
  })
})

describe('resolveHomePath', () => {
  it('returns a stored path that is in the allowlist', () => {
    expect(resolveHomePath('/pomodoro')).toBe('/pomodoro')
    expect(resolveHomePath('/budget')).toBe('/budget')
  })

  it('falls back to /budget for null, empty, or unknown values', () => {
    expect(resolveHomePath(null)).toBe('/budget')
    expect(resolveHomePath(undefined)).toBe('/budget')
    expect(resolveHomePath('')).toBe('/budget')
    expect(resolveHomePath('/not-a-real-route')).toBe('/budget')
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd frontend && npx vitest run src/pages/settings/homeOptions.test.ts`
Expected: FAIL — cannot resolve `./homeOptions`.

- [ ] **Step 3: Implement the lib**

Create `frontend/src/pages/settings/homeOptions.ts`:

```typescript
export type HomeOption = {
  path: string
  label: string
  requiresFamily: boolean
}

/** Top-level NavBar pages eligible as a Home page (ADR-084). */
export const HOME_OPTIONS: HomeOption[] = [
  { path: '/health', label: 'Health', requiresFamily: false },
  { path: '/pomodoro', label: 'Pomodoro', requiresFamily: false },
  { path: '/trips', label: 'Trips', requiresFamily: false },
  { path: '/recipes', label: 'Recipes', requiresFamily: true },
  { path: '/stock', label: 'Stock', requiresFamily: true },
  { path: '/meal-plan', label: 'Meal Plan', requiresFamily: true },
  { path: '/shopping', label: 'Shopping', requiresFamily: true },
  { path: '/budget', label: 'Budget', requiresFamily: true },
  { path: '/ai-assistant', label: 'AI', requiresFamily: true },
]

const DEFAULT_HOME = '/budget'

/** The family-aware selectable set: hide family-gated pages when the user has no family. */
export function homeOptions(hasFamily: boolean): HomeOption[] {
  return HOME_OPTIONS.filter((o) => hasFamily || !o.requiresFamily)
}

/**
 * Resolve where "/" should land: the stored HomePath when it is a known
 * home-eligible route, else the default (/budget). Family gating of the
 * resolved route is left to the route guards (ADR-084), keeping this a
 * loop-proof pure lookup.
 */
export function resolveHomePath(homePath: string | null | undefined): string {
  if (homePath && HOME_OPTIONS.some((o) => o.path === homePath)) {
    return homePath
  }
  return DEFAULT_HOME
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd frontend && npx vitest run src/pages/settings/homeOptions.test.ts`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/settings/homeOptions.ts frontend/src/pages/settings/homeOptions.test.ts
git commit -m "feat(settings): add home-options pure lib + tests (#39)"
```

---

### Task 5: RTK Query — `homePath` on MeDto + `updateUserSettings` mutation + `useCurrentUser`

**Files:**
- Modify: `frontend/src/shared/api/api.ts`
- Modify: `frontend/src/shared/hooks/useCurrentUser.ts`

**Interfaces:**
- Consumes: backend `GET /api/me` (Task 2), `PUT /api/me/settings` (Task 3).
- Produces: `useUpdateUserSettingsMutation`; `useCurrentUser().homePath` (`string | null`).

- [ ] **Step 1: Add `homePath` to the `MeDto` interface**

In `frontend/src/shared/api/api.ts`, extend the `MeDto` interface:

```typescript
export interface MeDto {
    userId: string
    email: string
    displayName: string
    familyId: string | null
    familyName: string | null
    familyInviteCode: string | null
    authProvider: string
    homePath: string | null
}
```

- [ ] **Step 2: Add the `updateUserSettings` mutation**

In the `endpoints: (build) => ({ ... })` block of `api` (beside `getMe`), add:

```typescript
        updateUserSettings: build.mutation<{ homePath: string | null }, { homePath: string | null }>({
            query: (body) => ({
                url: '/api/me/settings',
                method: 'PUT',
                body,
            }),
            // Changing the Home page changes what /api/me returns.
            invalidatesTags: ['Me'],
        }),
```

- [ ] **Step 3: Export the generated hook**

In the `export const { ... } = api` block, add `useUpdateUserSettingsMutation` beside `useGetMeQuery`:

```typescript
    useGetMeQuery,
    useUpdateUserSettingsMutation,
```

- [ ] **Step 4: Return `homePath` from `useCurrentUser`**

In `frontend/src/shared/hooks/useCurrentUser.ts`, add to the returned object (beside `familyId`):

```typescript
    homePath: me?.homePath ?? null,
```

- [ ] **Step 5: Typecheck + build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: no type errors; build succeeds.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/shared/api/api.ts frontend/src/shared/hooks/useCurrentUser.ts
git commit -m "feat(settings): wire homePath + updateUserSettings into RTK Query (#39)"
```

---

### Task 6: `HomeRedirect` + `/` route swap

**Files:**
- Create: `frontend/src/shared/components/HomeRedirect.tsx`
- Modify: `frontend/src/router.tsx`

**Interfaces:**
- Consumes: `useCurrentUser().homePath` + `isLoadingProfile` (Task 5), `resolveHomePath` (Task 4).

- [ ] **Step 1: Create the `HomeRedirect` component**

Create `frontend/src/shared/components/HomeRedirect.tsx`:

```tsx
import { Navigate } from 'react-router-dom'
import { useCurrentUser } from '../hooks/useCurrentUser'
import { resolveHomePath } from '../../pages/settings/homeOptions'

/**
 * Resolves "/" to the user's chosen Home page. Waits for /api/me to load
 * (so a family member is not briefly sent to the /budget default before
 * their homePath is known), then redirects. Route guards
 * (ProtectedRoute / FamilyRequiredRoute) then apply to the target.
 */
export function HomeRedirect() {
  const { homePath, isLoadingProfile } = useCurrentUser()

  if (isLoadingProfile) {
    return null
  }

  return <Navigate to={resolveHomePath(homePath)} replace />
}
```

- [ ] **Step 2: Swap the `/` route element**

In `frontend/src/router.tsx`:

Add the import beside the other shared-component imports (after the `FamilyRequiredRoute` import):

```tsx
import { HomeRedirect } from './shared/components/HomeRedirect'
```

Replace the `/` route line

```tsx
      { path: '/', element: <Navigate to="/budget" replace /> },
```

with

```tsx
      { path: '/', element: <HomeRedirect /> },
```

(Leave the `Navigate` import in place — the catch-all `{ path: '*', element: <Navigate to="/" replace /> }` still uses it.)

- [ ] **Step 3: Typecheck + build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: no type errors; build succeeds.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/shared/components/HomeRedirect.tsx frontend/src/router.tsx
git commit -m "feat(settings): resolve / via HomeRedirect against saved Home page (#39)"
```

---

### Task 7: `/settings` page + route + NavBar entry

**Files:**
- Create: `frontend/src/pages/settings/SettingsPage.tsx`
- Create: `frontend/src/pages/settings/SettingsPage.css`
- Create: `frontend/src/pages/settings/index.ts`
- Modify: `frontend/src/router.tsx`
- Modify: `frontend/src/shared/components/NavBar.tsx`

**Interfaces:**
- Consumes: `homeOptions` (Task 4), `useUpdateUserSettingsMutation` + `useCurrentUser` (Task 5).

- [ ] **Step 1: Create the settings page**

Create `frontend/src/pages/settings/SettingsPage.tsx`:

```tsx
import { useState } from 'react'
import { DropDownList } from '@syncfusion/react-dropdowns'
import type { ChangeEvent as DDLChangeEvent } from '@syncfusion/react-dropdowns'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { useUpdateUserSettingsMutation } from '../../shared/api/api'
import { homeOptions } from './homeOptions'
import './SettingsPage.css'

export function SettingsPage() {
  const { familyId, homePath } = useCurrentUser()
  const [updateSettings, { isLoading }] = useUpdateUserSettingsMutation()
  const [saved, setSaved] = useState(false)

  const options = homeOptions(!!familyId)
  const value = homePath ?? '/budget'

  const handleChange = async (e: DDLChangeEvent) => {
    const path = e.value as string
    setSaved(false)
    await updateSettings({ homePath: path }).unwrap()
    setSaved(true)
  }

  return (
    <section className="page page--settings">
      <header className="page__header">
        <h1>การตั้งค่า</h1>
      </header>

      <div className="settings-row">
        <div className="settings-row__label">
          <span className="settings-row__icon" aria-hidden="true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                 strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
              <path d="M4 11.5 12 4l8 7.5" />
              <path d="M6 10v9h12v-9" />
              <path d="M10 19v-5h4v5" />
            </svg>
          </span>
          <div>
            <div className="settings-row__title">หน้าแรก (Home page)</div>
            <div className="settings-row__sub">หน้าที่จะเปิดขึ้นมาเมื่อเข้าแอป</div>
          </div>
        </div>

        <DropDownList
          className="settings-home-ddl"
          dataSource={options}
          fields={{ text: 'label', value: 'path' }}
          value={value}
          onChange={handleChange}
        />
      </div>

      {saved && !isLoading && <p className="settings-saved">บันทึกแล้ว</p>}
    </section>
  )
}
```

- [ ] **Step 2: Create the page styles**

Create `frontend/src/pages/settings/SettingsPage.css`:

```css
.settings-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  border: 1px solid var(--color-border);
  border-radius: 10px;
  padding: 16px;
}
.settings-row__label {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  min-width: 200px;
}
.settings-row__icon {
  width: 30px;
  height: 30px;
  border-radius: 8px;
  background: #fff3e0;
  color: var(--color-primary);
  display: flex;
  align-items: center;
  justify-content: center;
  flex: none;
}
.settings-row__title { font-weight: 600; font-size: 15px; }
.settings-row__sub { font-size: 12.5px; color: var(--color-text-muted); margin-top: 2px; }
.settings-home-ddl { min-width: 220px; }
.settings-saved {
  margin-top: 12px;
  font-size: 13px;
  font-weight: 600;
  color: #2e7d32;
}
```

- [ ] **Step 3: Create the barrel**

Create `frontend/src/pages/settings/index.ts`:

```typescript
export { SettingsPage } from './SettingsPage'
```

- [ ] **Step 4: Add the `/settings` route (auth-only block)**

In `frontend/src/router.tsx`, add the import beside the other page barrels:

```tsx
import { SettingsPage } from './pages/settings'
```

Add the route inside the **first** `<AppLayout>` block (the auth-only one, alongside `/pomodoro` / `/trips`), e.g. after the `/pomodoro` line:

```tsx
          { path: '/settings', element: <SettingsPage /> },
```

- [ ] **Step 5: Add the NavBar "Settings" entry (desktop menu + mobile drawer)**

In `frontend/src/shared/components/NavBar.tsx`, in the desktop `app-navbar__account-menu` list, add a Settings item before the Sign out `<li>`:

```tsx
            <li>
              <NavLink to="/settings" role="menuitem">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                     strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"
                     style={{ verticalAlign: '-3px', marginRight: 8 }} aria-hidden="true">
                  <circle cx="12" cy="12" r="3.2" />
                  <path d="M12 2.5v3M12 18.5v3M4.2 7l2.6 1.5M17.2 15.5l2.6 1.5M4.2 17l2.6-1.5M17.2 8.5l2.6-1.5" />
                </svg>
                Settings
              </NavLink>
            </li>
```

And in the mobile drawer `app-drawer__links` list, add after the "Manage family" item:

```tsx
              <li>
                <NavLink to="/settings" className="app-drawer__link">
                  Settings
                </NavLink>
              </li>
```

- [ ] **Step 6: Typecheck + build**

Run: `cd frontend && npx tsc -b && npm run build`
Expected: no type errors; build succeeds.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/settings/SettingsPage.tsx \
  frontend/src/pages/settings/SettingsPage.css \
  frontend/src/pages/settings/index.ts \
  frontend/src/router.tsx \
  frontend/src/shared/components/NavBar.tsx
git commit -m "feat(settings): add /settings page + account-menu entry for Home page (closes #39)"
```

---

### Task 8: Apply the migration to prod + interactive verification

**Files:** none (deploy + manual verification).

- [ ] **Step 1: Preview the migration SQL**

Run (from `backend/`):

```bash
dotnet ef migrations script --idempotent \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

Expected: a `CREATE TABLE [UserSettings]` guarded by an `IF NOT EXISTS`-style version check. Review it.

- [ ] **Step 2: Add a temporary SQL firewall rule for your IP**

Run (replace `<IP>` with your current public IP; the address is named in the `Client with IP address ... is not allowed` error if you hit it):

```bash
az sql server firewall-rule create --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply \
  --start-ip-address <IP> --end-ip-address <IP>
```

- [ ] **Step 3: Apply the migration to prod**

Run (from `backend/`; requires the terminal `az` session to be `thodsaphonSP@hotmail.co.th`, the SQL Entra admin):

```bash
AZURE_TOKEN_CREDENTIALS=AzureCliCredential dotnet ef database update \
  --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi \
  --connection "Server=tcp:menunest-sql.database.windows.net,1433;Initial Catalog=MenuNest;Authentication=Active Directory Default;Encrypt=True;"
```

Expected: `Done.` and the `UserSettings` table exists in prod.

- [ ] **Step 4: Remove the temporary firewall rule**

```bash
az sql server firewall-rule delete --subscription 01473a32-351a-4cf5-9956-674d68e2ccbf \
  --resource-group MenuNest --server menunest-sql --name tmp-apply
```

- [ ] **Step 5: Interactive smoke test (CLAUDE.md — run the app / prod)**

Verify by hand:
- Open the account menu → **Settings** opens `/settings`.
- Pick a page (e.g. Pomodoro) → "บันทึกแล้ว" shows; reload the app at `/` → it lands on `/pomodoro`.
- As a **family-less** user, the dropdown shows only Health / Pomodoro / Trips and never dead-ends.
- Manually set an invalid stored value (or a family-gated page then leave the family) → `/` falls back to `/budget` (family-gated → `/join-family`), no redirect loop.

---

## Self-Review

**Spec coverage:** `UserSettings` 1:1 + `HomePath` (Task 1) ✓; `GET /api/me.homePath` (Task 2) ✓; `PUT /api/me/settings` command/validator/handler/controller (Task 3) ✓; `homeOptions`/`resolveHomePath` pure lib + tests (Task 4) ✓; RTK Query `homePath` + mutation + `useCurrentUser` (Task 5) ✓; `HomeRedirect` + `/` swap (Task 6) ✓; `/settings` page + route + NavBar (Task 7) ✓; manual migration + interactive smoke (Task 8) ✓. Family-aware selectable set + validated redirect default `/budget` (ADR-084) covered by Tasks 4/6/7.

**Placeholder scan:** none — every step carries real code/commands.

**Type consistency:** `MeDto.HomePath` (C#) ↔ `MeDto.homePath` (TS) mapped through the API; `UpdateUserSettingsCommand(string? HomePath)` ↔ mutation body `{ homePath }`; `resolveHomePath(homePath)` single-arg used identically in the lib, its test, and `HomeRedirect`; `homeOptions(hasFamily)` fields `{ label, path, requiresFamily }` match the DropDownList `fields={{ text: 'label', value: 'path' }}`.

**Deviations flagged:** (1) `resolveHomePath` drops the spec's unused `hasFamily` param (guards handle gating); (2) icons are inline SVG (not `@syncfusion/react-icons`, which is undeclared) — both satisfy "never emoji".
```
