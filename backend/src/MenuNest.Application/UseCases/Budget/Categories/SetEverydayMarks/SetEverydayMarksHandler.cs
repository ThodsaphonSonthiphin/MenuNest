using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Categories.SetEverydayMarks;

/// <summary>
/// Applies every mark in the request, saves once, then re-freezes the Daily
/// allowance once for the whole sheet (menunest-184) — this is what makes the
/// bulk shape a single Budgeting event rather than one per envelope. A request
/// that changes no envelope's <c>IsEveryday</c> value is not a Budgeting event
/// either — it must not touch the frozen figure.
/// </summary>
public sealed class SetEverydayMarksHandler : ICommandHandler<SetEverydayMarksCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<SetEverydayMarksCommand> _validator;
    private readonly AllowanceFreezer _freezer;
    private readonly IClock _clock;
    private readonly BudgetChangeRecorder _recorder;

    public SetEverydayMarksHandler(
        IApplicationDbContext db,
        IUserProvisioner users,
        IValidator<SetEverydayMarksCommand> validator,
        AllowanceFreezer freezer,
        IClock clock,
        BudgetChangeRecorder recorder)
    { _db = db; _users = users; _validator = validator; _freezer = freezer; _clock = clock; _recorder = recorder; }

    public async ValueTask<Unit> Handle(SetEverydayMarksCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var ids = cmd.Marks.Select(m => m.CategoryId).Distinct().ToList();
        var categories = await _db.BudgetCategories
            .Where(c => c.FamilyId == familyId && ids.Contains(c.Id))
            .ToListAsync(ct);
        if (categories.Count != ids.Count)
            throw new DomainException("Category not found.");

        // MarkEveryday has no change detection of its own (it always stamps
        // UpdatedAt), so the handler must notice for itself whether anything
        // actually flipped — otherwise a sheet the caller submits unchanged
        // (e.g. the SPA re-posting on open+close) would silently reset
        // FrozenOn and re-divide the pot over however many days are left.
        var flipped = new List<EverydayMark>();
        foreach (var mark in cmd.Marks)
        {
            var category = categories.First(c => c.Id == mark.CategoryId);
            if (category.IsEveryday != mark.IsEveryday) flipped.Add(mark);
            category.MarkEveryday(mark.IsEveryday);
        }

        if (flipped.Count == 0) return Unit.Value;

        // One freeze for the whole sheet — not one per mark (menunest-184). The
        // viewer's time zone (menunest-189) is only resolved here, where it's
        // actually used — a no-op sheet returns above without ever needing it.
        // It is resolved BEFORE the save because the recorded change also needs
        // the budget month, and the command carries no year/month of its own.
        var tz = BudgetTimeZone.Resolve(cmd.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));

        // menunest-196: an everyday mark is undoable, one row per envelope that
        // ACTUALLY flipped — a sheet re-posted unchanged records nothing, the
        // same rule the freeze above follows.
        foreach (var mark in flipped)
        {
            _recorder.Record(BudgetChange.RecordEverydayMark(
                familyId, user.Id, today.Year, today.Month, mark.CategoryId, mark.IsEveryday));
        }

        // One save for the whole sheet — not one per mark.
        await _db.SaveChangesAsync(ct);
        var refrozen = await _freezer.RefreezeAsync(familyId, today, ct);
        if (refrozen is not null) await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
