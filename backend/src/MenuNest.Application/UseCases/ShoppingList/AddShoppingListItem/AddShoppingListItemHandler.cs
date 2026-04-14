using FluentValidation;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.ShoppingList.AddShoppingListItem;

public sealed class AddShoppingListItemHandler
    : ICommandHandler<AddShoppingListItemCommand, ShoppingListItemDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IValidator<AddShoppingListItemCommand> _validator;

    public AddShoppingListItemHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IValidator<AddShoppingListItemCommand> validator)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _validator = validator;
    }

    public async ValueTask<ShoppingListItemDto> Handle(
        AddShoppingListItemCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        var (_, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == command.ListId && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        // Check if item for this ingredient already exists
        var existingItem = list.Items.FirstOrDefault(i => i.IngredientId == command.IngredientId);

        ShoppingListItemDto result;
        if (existingItem is not null)
        {
            // Update quantity directly on existing tracked item
            existingItem.UpdateQuantity(existingItem.Quantity + command.Quantity);
            await _db.SaveChangesAsync(ct);

            var ingredient = await _db.Ingredients.FindAsync(new object[] { existingItem.IngredientId }, ct);
            result = new ShoppingListItemDto(
                existingItem.Id, existingItem.IngredientId, ingredient!.Name, ingredient.Unit,
                existingItem.Quantity, existingItem.IsBought, existingItem.BoughtAt,
                existingItem.SourceMealPlanEntryIds.Count > 0 ? existingItem.SourceMealPlanEntryIds : null);
        }
        else
        {
            // New item — must explicitly add to DbSet for InMemory tracking
            var newItem = list.AddOrIncreaseItem(command.IngredientId, command.Quantity);
            _db.ShoppingListItems.Add(newItem);
            await _db.SaveChangesAsync(ct);

            var ingredient = await _db.Ingredients.FindAsync(new object[] { newItem.IngredientId }, ct);
            result = new ShoppingListItemDto(
                newItem.Id, newItem.IngredientId, ingredient!.Name, ingredient.Unit,
                newItem.Quantity, newItem.IsBought, newItem.BoughtAt,
                newItem.SourceMealPlanEntryIds.Count > 0 ? newItem.SourceMealPlanEntryIds : null);
        }

        return result;
    }
}
