using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Categories.CreateCategory;

public sealed class CreateCategoryHandler : ICommandHandler<CreateCategoryCommand, BudgetCategoryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateCategoryCommand> _validator;
    public CreateCategoryHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateCategoryCommand> v)
    { _db = db; _users = users; _validator = v; }

    public async ValueTask<BudgetCategoryDto> Handle(CreateCategoryCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var groupBelongs = await _db.BudgetCategoryGroups
            .AnyAsync(g => g.Id == cmd.GroupId && g.FamilyId == familyId, ct);
        if (!groupBelongs) throw new DomainException("Group not found.");

        var cat = BudgetCategory.Create(familyId, cmd.GroupId, cmd.Name, cmd.Emoji, cmd.SortOrder);
        ApplyTarget(cat, cmd.TargetType, cmd.TargetAmount, cmd.TargetDueDate, cmd.TargetDayOfMonth);

        _db.BudgetCategories.Add(cat);
        await _db.SaveChangesAsync(ct);
        return new BudgetCategoryDto(
            cat.Id, cat.GroupId, cat.Name, cat.Emoji, cat.SortOrder, cat.IsHidden,
            cat.TargetType, cat.TargetAmount, cat.TargetDueDate, cat.TargetDayOfMonth);
    }

    internal static void ApplyTarget(
        BudgetCategory cat,
        BudgetTargetType targetType,
        decimal? targetAmount,
        DateOnly? targetDueDate,
        int? targetDayOfMonth)
    {
        switch (targetType)
        {
            case BudgetTargetType.None:
                cat.ClearTarget();
                break;
            case BudgetTargetType.MonthlyAmount:
                cat.SetMonthlyTarget(targetAmount!.Value, targetDayOfMonth);
                break;
            case BudgetTargetType.ByDate:
                cat.SetByDateTarget(targetAmount!.Value, targetDueDate!.Value);
                break;
            case BudgetTargetType.MonthlySavingsBuilder:
                cat.SetMonthlySavingsBuilderTarget(targetAmount!.Value);
                break;
        }
    }
}
