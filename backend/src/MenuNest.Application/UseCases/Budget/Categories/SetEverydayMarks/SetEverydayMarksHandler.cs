using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Categories.SetEverydayMarks;

/// <summary>
/// Applies every mark in the request, saves once, then re-freezes the Daily
/// allowance once for the whole sheet (menunest-184) — this is what makes the
/// bulk shape a single Budgeting event rather than one per envelope.
/// </summary>
public sealed class SetEverydayMarksHandler : ICommandHandler<SetEverydayMarksCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly AllowanceFreezer _freezer;

    public SetEverydayMarksHandler(IApplicationDbContext db, IUserProvisioner users, AllowanceFreezer freezer)
    { _db = db; _users = users; _freezer = freezer; }

    public async ValueTask<Unit> Handle(SetEverydayMarksCommand cmd, CancellationToken ct)
    {
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var ids = cmd.Marks.Select(m => m.CategoryId).Distinct().ToList();
        var categories = await _db.BudgetCategories
            .Where(c => c.FamilyId == familyId && ids.Contains(c.Id))
            .ToListAsync(ct);
        if (categories.Count != ids.Count)
            throw new DomainException("Category not found.");

        foreach (var mark in cmd.Marks)
        {
            var category = categories.First(c => c.Id == mark.CategoryId);
            category.MarkEveryday(mark.IsEveryday);
        }

        // One save for the whole sheet — not one per mark.
        await _db.SaveChangesAsync(ct);

        // One freeze for the whole sheet — not one per mark (menunest-184).
        var refrozen = await _freezer.RefreezeAsync(familyId, DateOnly.FromDateTime(DateTime.UtcNow), ct);
        if (refrozen is not null) await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
