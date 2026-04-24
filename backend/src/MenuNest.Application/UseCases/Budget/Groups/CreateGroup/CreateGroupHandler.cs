using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UseCases.Budget.Groups.CreateGroup;

public sealed class CreateGroupHandler : ICommandHandler<CreateGroupCommand, CategoryGroupDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<CreateGroupCommand> _validator;
    public CreateGroupHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<CreateGroupCommand> v)
    { _db = db; _users = users; _validator = v; }

    public async ValueTask<CategoryGroupDto> Handle(CreateGroupCommand cmd, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(cmd, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        var group = BudgetCategoryGroup.Create(familyId, cmd.Name, cmd.SortOrder);
        _db.BudgetCategoryGroups.Add(group);
        await _db.SaveChangesAsync(ct);
        return new CategoryGroupDto(group.Id, group.Name, group.SortOrder, group.IsHidden);
    }
}
