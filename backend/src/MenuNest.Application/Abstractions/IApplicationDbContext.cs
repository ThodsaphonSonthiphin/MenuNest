using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.Abstractions;

/// <summary>
/// Application-layer view of the persistence store. Exposes only the
/// DbSets handlers actually need so Application never references the
/// concrete <c>AppDbContext</c> in Infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Family> Families { get; }
    DbSet<User> Users { get; }
    DbSet<UserRelationship> UserRelationships { get; }
    DbSet<Ingredient> Ingredients { get; }
    DbSet<Recipe> Recipes { get; }
    DbSet<RecipeIngredient> RecipeIngredients { get; }
    DbSet<StockItem> StockItems { get; }
    DbSet<StockTransaction> StockTransactions { get; }
    DbSet<MealPlanEntry> MealPlanEntries { get; }
    DbSet<ShoppingList> ShoppingLists { get; }
    DbSet<ShoppingListItem> ShoppingListItems { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
