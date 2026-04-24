using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Monthly.MoveMoney;

public sealed class MoveMoneyHandler : ICommandHandler<MoveMoneyCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<MoveMoneyCommand> _validator;

    public MoveMoneyHandler(
        IApplicationDbContext db,
        IUserProvisioner users,
        IValidator<MoveMoneyCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<Unit> Handle(MoveMoneyCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var from = await GetOrCreateAsync(familyId, cmd.FromCategoryId, cmd.Year, cmd.Month, ct);
        var to = await GetOrCreateAsync(familyId, cmd.ToCategoryId, cmd.Year, cmd.Month, ct);

        from.AdjustAmount(-cmd.Amount);
        to.AdjustAmount(+cmd.Amount);
        await _db.SaveChangesAsync(ct);
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
