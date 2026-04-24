using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.Persistence;

/// <summary>
/// The EF Core context for MenuNest. Schema is defined by
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes under
/// <see cref="Configurations"/> and picked up via
/// <see cref="ModelBuilder.ApplyConfigurationsFromAssembly(System.Reflection.Assembly, Func{Type, bool}?)"/>.
/// Implements <see cref="IApplicationDbContext"/> so Application-layer
/// handlers can depend on the abstraction instead of the concrete type.
/// </summary>
public sealed class AppDbContext : DbContext, IApplicationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Family> Families => Set<Family>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRelationship> UserRelationships => Set<UserRelationship>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<MealPlanEntry> MealPlanEntries => Set<MealPlanEntry>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<BudgetAccount> BudgetAccounts => Set<BudgetAccount>();
    public DbSet<BudgetCategoryGroup> BudgetCategoryGroups => Set<BudgetCategoryGroup>();
    public DbSet<BudgetCategory> BudgetCategories => Set<BudgetCategory>();
    public DbSet<MonthlyAssignment> MonthlyAssignments => Set<MonthlyAssignment>();
    public DbSet<MonthlyIncome> MonthlyIncomes => Set<MonthlyIncome>();
    public DbSet<BudgetTransaction> BudgetTransactions => Set<BudgetTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
