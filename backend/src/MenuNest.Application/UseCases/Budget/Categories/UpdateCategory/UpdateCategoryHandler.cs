using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Application.UseCases.Budget.Categories.CreateCategory;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Categories.UpdateCategory;

public sealed class UpdateCategoryHandler : ICommandHandler<UpdateCategoryCommand, BudgetCategoryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateCategoryCommand> _v;
    public UpdateCategoryHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateCategoryCommand> v)
    { _db = db; _users = users; _v = v; }

    public async ValueTask<BudgetCategoryDto> Handle(UpdateCategoryCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);

        var cat = await _db.BudgetCategories
            .FirstOrDefaultAsync(x => x.Id == c.Id && x.FamilyId == familyId, ct)
            ?? throw new DomainException("Category not found.");

        var groupBelongs = await _db.BudgetCategoryGroups
            .AnyAsync(g => g.Id == c.GroupId && g.FamilyId == familyId, ct);
        if (!groupBelongs) throw new DomainException("Group not found.");

        cat.Update(c.Name, c.Emoji, c.GroupId, c.SortOrder);
        CreateCategoryHandler.ApplyTarget(cat, c.TargetType, c.TargetAmount, c.TargetDueDate, c.TargetDayOfMonth);

        await _db.SaveChangesAsync(ct);
        return new BudgetCategoryDto(
            cat.Id, cat.GroupId, cat.Name, cat.Emoji, cat.SortOrder, cat.IsHidden,
            cat.TargetType, cat.TargetAmount, cat.TargetDueDate, cat.TargetDayOfMonth);
    }
}
