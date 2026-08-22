using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;

public sealed class MoveMoneyHandler : ICommandHandler<MoveMoneyCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<MoveMoneyCommand> _validator;
    private readonly AllowanceFreezer _freezer;

    public MoveMoneyHandler(
        IApplicationDbContext db,
        IUserProvisioner users,
        IValidator<MoveMoneyCommand> validator,
        AllowanceFreezer freezer)
    { _db = db; _users = users; _validator = validator; _freezer = freezer; }

    public async ValueTask<Unit> Handle(MoveMoneyCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var from = await GetOrCreateAsync(familyId, cmd.FromCategoryId, cmd.Year, cmd.Month, ct);
        var to = await GetOrCreateAsync(familyId, cmd.ToCategoryId, cmd.Year, cmd.Month, ct);

        from.AdjustAmount(-cmd.Amount);
        to.AdjustAmount(+cmd.Amount);
        await _db.SaveChangesAsync(ct);

        // menunest-181: only re-freeze when an everyday envelope is actually
        // involved — a move between two non-everyday envelopes is not a
        // Budgeting event for this purpose.
        var touchesEveryday = await _db.BudgetCategories.AnyAsync(
            c => c.FamilyId == familyId && c.IsEveryday
              && (c.Id == cmd.FromCategoryId || c.Id == cmd.ToCategoryId), ct);
        if (touchesEveryday)
        {
            var refrozen = await _freezer.RefreezeAsync(familyId, DateOnly.FromDateTime(DateTime.UtcNow), ct);
            if (refrozen is not null) await _db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }

    private async Task<MonthlyAssignment> GetOrCreateAsync(
        Guid familyId, Guid categoryId, int year, int month, CancellationToken ct)
    {
        var row = await _db.MonthlyAssignments.FirstOrDefaultAsync(
            x => x.FamilyId == familyId && x.CategoryId == categoryId
              && x.Year == year && x.Month == month, ct);
        if (row is not null) return row;

        var belongs = await _db.BudgetCategories.AnyAsync(
            c => c.Id == categoryId && c.FamilyId == familyId, ct);
        if (!belongs) throw new DomainException("Category not found.");

        var created = MonthlyAssignment.Create(familyId, categoryId, year, month, 0m);
        _db.MonthlyAssignments.Add(created);
        return created;
    }
}
