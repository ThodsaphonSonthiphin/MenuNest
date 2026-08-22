using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
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

    public SetEverydayMarksHandler(
        IApplicationDbContext db,
        IUserProvisioner users,
        IValidator<SetEverydayMarksCommand> validator,
        AllowanceFreezer freezer)
    { _db = db; _users = users; _validator = validator; _freezer = freezer; }

    public async ValueTask<Unit> Handle(SetEverydayMarksCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

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
        var changed = false;
        foreach (var mark in cmd.Marks)
        {
            var category = categories.First(c => c.Id == mark.CategoryId);
            if (category.IsEveryday != mark.IsEveryday) changed = true;
            category.MarkEveryday(mark.IsEveryday);
        }

        if (!changed) return Unit.Value;

        // One save for the whole sheet — not one per mark.
        await _db.SaveChangesAsync(ct);

        // One freeze for the whole sheet — not one per mark (menunest-184).
        var refrozen = await _freezer.RefreezeAsync(familyId, DateOnly.FromDateTime(DateTime.UtcNow), ct);
        if (refrozen is not null) await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
