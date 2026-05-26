using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Budget.Groups.UpdateGroup;

public sealed class UpdateGroupHandler : ICommandHandler<UpdateGroupCommand, CategoryGroupDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _users;
    private readonly IValidator<UpdateGroupCommand> _v;
    public UpdateGroupHandler(IApplicationDbContext db, IUserProvisioner users, IValidator<UpdateGroupCommand> v)
    { _db = db; _users = users; _v = v; }

    public async ValueTask<CategoryGroupDto> Handle(UpdateGroupCommand c, CancellationToken ct)
    {
        await _v.ValidateAndThrowAsync(c, ct);
        var (_, familyId) = await _users.RequireFamilyAsync(ct);
        var group = await _db.BudgetCategoryGroups
            .FirstOrDefaultAsync(g => g.Id == c.Id && g.FamilyId == familyId, ct)
            ?? throw new DomainException("Group not found.");

        var trimmed = c.Name.Trim();
        var nameTaken = await _db.BudgetCategoryGroups
            .Where(g => g.FamilyId == familyId && g.Id != c.Id)
            .AnyAsync(g => g.Name.ToLower() == trimmed.ToLower(), ct);
        if (nameTaken)
            throw new DomainException($"A group named \"{trimmed}\" already exists.");

        group.Rename(c.Name);
        group.SetSortOrder(c.SortOrder);
        await _db.SaveChangesAsync(ct);
        return new CategoryGroupDto(group.Id, group.Name, group.SortOrder, group.IsHidden);
    }
}
