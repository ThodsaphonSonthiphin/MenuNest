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
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
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

    // MCP OAuth proxy durable store (ADR-037)
    public DbSet<OAuthClient> OAuthClients => Set<OAuthClient>();
    public DbSet<OAuthRefreshToken> OAuthRefreshTokens => Set<OAuthRefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
