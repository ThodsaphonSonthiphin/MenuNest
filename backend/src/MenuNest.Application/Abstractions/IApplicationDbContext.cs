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
    DbSet<ChatConversation> ChatConversations { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<BudgetAccount> BudgetAccounts { get; }
    DbSet<BudgetCategoryGroup> BudgetCategoryGroups { get; }
    DbSet<BudgetCategory> BudgetCategories { get; }
    DbSet<MonthlyAssignment> MonthlyAssignments { get; }
    DbSet<BudgetTransaction> BudgetTransactions { get; }

    // Health (migraine tracker) module
    DbSet<Drug> Drugs { get; }
    DbSet<Symptom> Symptoms { get; }
    DbSet<Trigger> Triggers { get; }
    DbSet<SymptomEpisode> SymptomEpisodes { get; }
    DbSet<Intake> Intakes { get; }
    DbSet<FollowUpPing> FollowUpPings { get; }
    DbSet<WebPushSubscription> WebPushSubscriptions { get; }
    DbSet<ShareLink> ShareLinks { get; }
    DbSet<Photo> Photos { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
