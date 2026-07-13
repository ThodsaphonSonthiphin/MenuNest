using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UnitTests.Support;

/// <summary>
/// A relational (SQLite) <see cref="IApplicationDbContext"/> that applies the *real*
/// Infrastructure entity configurations — so real constraints like the unique
/// (TripId, Date) index on <see cref="ItineraryDay"/> are exercised — but rewrites
/// the handful of SQL Server-only <c>nvarchar(max)</c> column types that SQLite's DDL
/// parser rejects. Used by relational handler tests that must observe unique-index
/// and per-statement behaviour the InMemory provider silently ignores.
/// </summary>
public sealed class SqliteAppDbContext : DbContext, IApplicationDbContext
{
    public SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options) : base(options) { }

    public DbSet<Family> Families => Set<Family>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRelationship> UserRelationships => Set<UserRelationship>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<MealPlanEntry> MealPlanEntries => Set<MealPlanEntry>();
    public DbSet<Domain.Entities.ShoppingList> ShoppingLists => Set<Domain.Entities.ShoppingList>();
    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<BudgetAccount> BudgetAccounts => Set<BudgetAccount>();
    public DbSet<BudgetCategoryGroup> BudgetCategoryGroups => Set<BudgetCategoryGroup>();
    public DbSet<BudgetCategory> BudgetCategories => Set<BudgetCategory>();
    public DbSet<MonthlyAssignment> MonthlyAssignments => Set<MonthlyAssignment>();
    public DbSet<BudgetTransaction> BudgetTransactions => Set<BudgetTransaction>();

    // Health (migraine tracker) module
    public DbSet<Drug> Drugs => Set<Drug>();
    public DbSet<Symptom> Symptoms => Set<Symptom>();
    public DbSet<Trigger> Triggers => Set<Trigger>();
    public DbSet<SymptomEpisode> SymptomEpisodes => Set<SymptomEpisode>();
    public DbSet<Intake> Intakes => Set<Intake>();
    public DbSet<FollowUpPing> FollowUpPings => Set<FollowUpPing>();
    public DbSet<WebPushSubscription> WebPushSubscriptions => Set<WebPushSubscription>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<Photo> Photos => Set<Photo>();

    // Trip Planner module
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripPlace> TripPlaces => Set<TripPlace>();
    public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
    public DbSet<Stop> Stops => Set<Stop>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<PlaceChecklistEntry> PlaceChecklistEntries => Set<PlaceChecklistEntry>();
    public DbSet<PlaceProfile> PlaceProfiles => Set<PlaceProfile>();
    public DbSet<PlaceProfileChecklistItem> PlaceProfileChecklistItems => Set<PlaceProfileChecklistItem>();

    public new Task<int> SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // SQLite's CREATE TABLE parser rejects the SQL Server "(max)" length token
        // (e.g. nvarchar(max)). Drop those explicit column types so the store type
        // falls back to SQLite's TEXT convention; nothing under test depends on it.
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties()))
        {
            var columnType = property.GetColumnType();
            if (columnType is not null &&
                columnType.Contains("(max)", StringComparison.OrdinalIgnoreCase))
            {
                property.SetColumnType(null);
            }
        }
    }
}
