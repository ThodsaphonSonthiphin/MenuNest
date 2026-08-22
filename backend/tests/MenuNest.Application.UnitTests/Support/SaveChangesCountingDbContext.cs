using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UnitTests.Support;

/// <summary>
/// Thin <see cref="IApplicationDbContext"/> decorator that counts
/// <see cref="SaveChangesAsync"/> calls and otherwise forwards everything to
/// an inner context. Exists so a test can prove a handler performs a FIXED
/// number of save round-trips regardless of how many items it processes —
/// e.g. a bulk mark of N envelopes must save/freeze once, not N times. A
/// state-only assertion (the final row's contents) cannot tell "one freeze
/// after all N marks" apart from "one freeze per mark, ending on the Nth",
/// because both converge on the same final row; only the call count does.
/// </summary>
public sealed class SaveChangesCountingDbContext(IApplicationDbContext inner) : IApplicationDbContext
{
    public int SaveChangesCallCount { get; private set; }

    public DbSet<Family> Families => inner.Families;
    public DbSet<User> Users => inner.Users;
    public DbSet<UserSettings> UserSettings => inner.UserSettings;
    public DbSet<UserRelationship> UserRelationships => inner.UserRelationships;
    public DbSet<Ingredient> Ingredients => inner.Ingredients;
    public DbSet<Recipe> Recipes => inner.Recipes;
    public DbSet<RecipeIngredient> RecipeIngredients => inner.RecipeIngredients;
    public DbSet<StockItem> StockItems => inner.StockItems;
    public DbSet<StockTransaction> StockTransactions => inner.StockTransactions;
    public DbSet<MealPlanEntry> MealPlanEntries => inner.MealPlanEntries;
    public DbSet<Domain.Entities.ShoppingList> ShoppingLists => inner.ShoppingLists;
    public DbSet<ShoppingListItem> ShoppingListItems => inner.ShoppingListItems;
    public DbSet<ChatConversation> ChatConversations => inner.ChatConversations;
    public DbSet<ChatMessage> ChatMessages => inner.ChatMessages;
    public DbSet<BudgetAccount> BudgetAccounts => inner.BudgetAccounts;
    public DbSet<BudgetCategoryGroup> BudgetCategoryGroups => inner.BudgetCategoryGroups;
    public DbSet<BudgetCategory> BudgetCategories => inner.BudgetCategories;
    public DbSet<MonthlyAssignment> MonthlyAssignments => inner.MonthlyAssignments;
    public DbSet<BudgetTransaction> BudgetTransactions => inner.BudgetTransactions;
    public DbSet<DailyAllowance> DailyAllowances => inner.DailyAllowances;

    public DbSet<Drug> Drugs => inner.Drugs;
    public DbSet<Symptom> Symptoms => inner.Symptoms;
    public DbSet<Trigger> Triggers => inner.Triggers;
    public DbSet<SymptomEpisode> SymptomEpisodes => inner.SymptomEpisodes;
    public DbSet<Intake> Intakes => inner.Intakes;
    public DbSet<FollowUpPing> FollowUpPings => inner.FollowUpPings;
    public DbSet<WebPushSubscription> WebPushSubscriptions => inner.WebPushSubscriptions;
    public DbSet<ShareLink> ShareLinks => inner.ShareLinks;
    public DbSet<Photo> Photos => inner.Photos;

    public DbSet<Trip> Trips => inner.Trips;
    public DbSet<TripPlace> TripPlaces => inner.TripPlaces;
    public DbSet<ItineraryDay> ItineraryDays => inner.ItineraryDays;
    public DbSet<Stop> Stops => inner.Stops;
    public DbSet<ChecklistItem> ChecklistItems => inner.ChecklistItems;
    public DbSet<StopChecklistEntry> StopChecklistEntries => inner.StopChecklistEntries;
    public DbSet<PlaceProfile> PlaceProfiles => inner.PlaceProfiles;
    public DbSet<PlaceProfileChecklistItem> PlaceProfileChecklistItems => inner.PlaceProfileChecklistItems;

    public DbSet<OAuthClient> OAuthClients => inner.OAuthClients;
    public DbSet<OAuthRefreshToken> OAuthRefreshTokens => inner.OAuthRefreshTokens;

    public DbSet<AppSession> AppSessions => inner.AppSessions;

    public DbSet<WritingEntry> WritingEntries => inner.WritingEntries;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCallCount++;
        return inner.SaveChangesAsync(ct);
    }
}
