using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Monthly.SetMonthlyIncome;

public sealed class SetMonthlyIncomeHandler : ICommandHandler<SetMonthlyIncomeCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<SetMonthlyIncomeCommand> _validator;

    public SetMonthlyIncomeHandler(
        IApplicationDbContext db,
        IUserProvisioner users,
        IValidator<SetMonthlyIncomeCommand> validator)
    { _db = db; _users = users; _validator = validator; }

    public async ValueTask<Unit> Handle(SetMonthlyIncomeCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var row = await _db.MonthlyIncomes.FirstOrDefaultAsync(
            x => x.FamilyId == familyId && x.Year == cmd.Year && x.Month == cmd.Month, ct);
        if (row is null)
            _db.MonthlyIncomes.Add(MonthlyIncome.Create(familyId, cmd.Year, cmd.Month, cmd.Amount));
        else
            row.SetAmount(cmd.Amount);

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
