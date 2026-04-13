using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.MealPlan.DeleteMealPlanEntry;

public sealed class DeleteMealPlanEntryHandler : ICommandHandler<DeleteMealPlanEntryCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteMealPlanEntryHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(DeleteMealPlanEntryCommand command, CancellationToken ct)
    {
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var entry = await _db.MealPlanEntries
            .FirstOrDefaultAsync(m => m.Id == command.Id && m.FamilyId == familyId, ct)
            ?? throw new DomainException("Meal plan entry not found.");

        if (entry.Status == MealEntryStatus.Cooked)
        {
            throw new DomainException("Cannot delete a cooked meal — undo the cook first to restore stock.");
        }

        _db.MealPlanEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
