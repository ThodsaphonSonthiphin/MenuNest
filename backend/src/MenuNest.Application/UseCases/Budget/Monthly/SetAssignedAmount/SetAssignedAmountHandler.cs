using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;

public sealed class SetAssignedAmountHandler : ICommandHandler<SetAssignedAmountCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<SetAssignedAmountCommand> _validator;

    public SetAssignedAmountHandler(
        IApplicationDbContext db,
        IUserProvisioner users,
        IValidator<SetAssignedAmountCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<Unit> Handle(SetAssignedAmountCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var exists = await _db.BudgetCategories.AnyAsync(
            x => x.Id == cmd.CategoryId && x.FamilyId == familyId, ct);
        if (!exists) throw new DomainException("Category not found.");

        var row = await _db.MonthlyAssignments.FirstOrDefaultAsync(
            x => x.FamilyId == familyId && x.CategoryId == cmd.CategoryId
              && x.Year == cmd.Year && x.Month == cmd.Month, ct);
        if (row is null)
            _db.MonthlyAssignments.Add(
                MonthlyAssignment.Create(familyId, cmd.CategoryId, cmd.Year, cmd.Month, cmd.Amount));
        else
            row.SetAmount(cmd.Amount);

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
