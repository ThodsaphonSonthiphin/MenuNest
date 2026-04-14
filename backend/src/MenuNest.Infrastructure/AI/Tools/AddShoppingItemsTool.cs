using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

// Adaptation: Plan called list.AddItem(name, quantity, unit) — that method does not exist.
// Actual method is AddOrIncreaseItem(ingredientId, quantity).
// We find-or-create the Ingredient, then call AddOrIncreaseItem with its ID.
public sealed class AddShoppingItemsTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public AddShoppingItemsTool(IApplicationDbContext db) => _db = db;

    public string Name => "add_shopping_items";
    public string Description => "เพิ่มรายการของที่ต้องซื้อเข้า shopping list";
    public bool RequiresConfirmation => true;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "shoppingListId": { "type": "string", "format": "uuid", "description": "ID ของ shopping list" },
            "items": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string", "description": "ชื่อของ" },
                        "quantity": { "type": "number", "description": "จำนวน" },
                        "unit": { "type": "string", "description": "หน่วย" }
                    },
                    "required": ["name"]
                }
            }
        },
        "required": ["shoppingListId", "items"]
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct)
    {
        var listId = Guid.Parse(arguments.GetProperty("shoppingListId").GetString()!);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == listId && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        var items = arguments.GetProperty("items");
        var addedCount = 0;

        foreach (var item in items.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var quantity = item.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 1m;
            var unit = item.TryGetProperty("unit", out var u) ? u.GetString() ?? "unit" : "unit";

            // Find or create an Ingredient for this family with the given name
            var ingredient = await _db.Ingredients
                .FirstOrDefaultAsync(i => i.FamilyId == familyId && i.Name == name, ct);

            if (ingredient is null)
            {
                ingredient = Ingredient.Create(familyId, name, unit);
                _db.Ingredients.Add(ingredient);
                await _db.SaveChangesAsync(ct);
            }

            list.AddOrIncreaseItem(ingredient.Id, quantity);
            addedCount++;
        }

        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new { success = true, addedCount, totalItems = list.Items.Count });
    }
}
