using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Allowance;
using MenuNest.Application.UseCases.Budget.History;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Monthly.SetAssignedAmount;

public sealed class SetAssignedAmountHandler : ICommandHandler<SetAssignedAmountCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<SetAssignedAmountCommand> _validator;
    private readonly AllowanceFreezer _freezer;
    private readonly IClock _clock;
    private readonly BudgetChangeRecorder _recorder;

    public SetAssignedAmountHandler(
        IApplicationDbContext db,
        IUserProvisioner users,
        IValidator<SetAssignedAmountCommand> validator,
        AllowanceFreezer freezer,
        IClock clock,
        BudgetChangeRecorder recorder)
    { _db = db; _users = users; _validator = validator; _freezer = freezer; _clock = clock; _recorder = recorder; }

    public async ValueTask<Unit> Handle(SetAssignedAmountCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (user, familyId) = await _users.RequireFamilyAsync(ct);

        var category = await _db.BudgetCategories.FirstOrDefaultAsync(
            x => x.Id == cmd.CategoryId && x.FamilyId == familyId, ct);
        if (category is null) throw new DomainException("Category not found.");

        var row = await _db.MonthlyAssignments.FirstOrDefaultAsync(
            x => x.FamilyId == familyId && x.CategoryId == cmd.CategoryId
              && x.Year == cmd.Year && x.Month == cmd.Month, ct);
        var previous = row?.AssignedAmount ?? 0m;
        var delta = cmd.Amount - previous;

        if (row is null)
            _db.MonthlyAssignments.Add(
                MonthlyAssignment.Create(familyId, cmd.CategoryId, cmd.Year, cmd.Month, cmd.Amount));
        else
            row.SetAmount(cmd.Amount);

        // menunest-193: record the DELTA, never the before/after values. Undo
        // applies the opposite delta as a new write, so a concurrent change by
        // another Family member survives it. A no-op assign records nothing.
        if (delta != 0m)
        {
            _recorder.Record(BudgetChange.RecordAssign(
                familyId, user.Id, cmd.Year, cmd.Month, cmd.CategoryId, delta, cmd.BatchId));
        }

        await _db.SaveChangesAsync(ct);

        // menunest-181/189: assigning money into an everyday envelope is a
        // Budgeting event. A non-everyday envelope never touches the freeze —
        // and so never needs the viewer's time zone either (only resolved here,
        // where it's actually used, matching ADR-038's Trips pattern).
        if (category.IsEveryday)
        {
            var tz = BudgetTimeZone.Resolve(cmd.TimeZoneId);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, tz));
            var refrozen = await _freezer.RefreezeAsync(familyId, today, ct);
            if (refrozen is not null) await _db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}
