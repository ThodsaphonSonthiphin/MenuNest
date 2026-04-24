using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace MenuNest.Application.UnitTests.Support;

/// <summary>
/// EF Core InMemory implementation of <see cref="IApplicationDbContext"/>.
/// Used only by Application unit tests so the handlers under test can run
/// against real DbSet/IQueryable plumbing without needing the real
/// <c>AppDbContext</c> from Infrastructure.
/// </summary>
public sealed class InMemoryAppDbContext : DbContext, IApplicationDbContext
{
    public InMemoryAppDbContext(DbContextOptions<InMemoryAppDbContext> options) : base(options) { }

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
    public DbSet<MonthlyIncome> MonthlyIncomes => Set<MonthlyIncome>();
    public DbSet<BudgetTransaction> BudgetTransactions => Set<BudgetTransaction>();

    public new Task<int> SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mirror the value conversion for InviteCode so the InMemory provider
        // can materialise the value object (its constructor is private).
        modelBuilder.Entity<Family>()
            .Property(f => f.InviteCode)
            .HasConversion(
                ic => ic.Value,
                raw => InviteCode.From(raw));

        // Mirror the JSON-list conversion for ShoppingListItem so EF can
        // round-trip the IReadOnlyList<Guid> in the InMemory store.
        var sourceIdsComparer = new ValueComparer<IReadOnlyList<Guid>>(
            (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
            v => v.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
            v => v.ToList());

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.General);
        modelBuilder.Entity<ShoppingListItem>()
            .Property(i => i.SourceMealPlanEntryIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => (IReadOnlyList<Guid>)(JsonSerializer.Deserialize<List<Guid>>(v, jsonOptions) ?? new List<Guid>()),
                sourceIdsComparer);

        // Mirror the field-access navigation so EF tracks items added via AddOrIncreaseItem.
        modelBuilder.Entity<Domain.Entities.ShoppingList>()
            .HasMany(l => l.Items)
            .WithOne()
            .HasForeignKey(i => i.ShoppingListId);

        modelBuilder.Entity<Domain.Entities.ShoppingList>()
            .Metadata
            .FindNavigation(nameof(Domain.Entities.ShoppingList.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
