using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

// Recipe.Create(familyId, name, createdByUserId, description) — matches actual signature.
// Ingredient.Create(familyId, name, unit) — matches actual signature.
public sealed class CreateRecipeTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public CreateRecipeTool(IApplicationDbContext db) => _db = db;

    public string Name => "create_recipe";
    public string Description => "สร้างสูตรอาหารใหม่พร้อมวัตถุดิบ";
    public bool RequiresConfirmation => true;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "name": { "type": "string", "description": "ชื่อเมนู" },
            "description": { "type": "string", "description": "คำอธิบายสั้นๆ" },
            "ingredients": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "unit": { "type": "string" },
                        "quantity": { "type": "number" }
                    },
                    "required": ["name", "unit", "quantity"]
                },
                "description": "รายการวัตถุดิบ"
            }
        },
        "required": ["name", "ingredients"]
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct)
    {
        var name = arguments.GetProperty("name").GetString()!;
        var description = arguments.TryGetProperty("description", out var desc) ? desc.GetString() : null;

        var recipe = Recipe.Create(familyId, name, userId, description);

        if (arguments.TryGetProperty("ingredients", out var ingredients))
        {
            foreach (var ing in ingredients.EnumerateArray())
            {
                var ingName = ing.GetProperty("name").GetString()!;
                var ingUnit = ing.GetProperty("unit").GetString()!;
                var quantity = ing.GetProperty("quantity").GetDecimal();

                // Find or create ingredient for this family
                var ingredient = await _db.Ingredients
                    .FirstOrDefaultAsync(i => i.FamilyId == familyId && i.Name == ingName, ct);

                if (ingredient is null)
                {
                    ingredient = Ingredient.Create(familyId, ingName, ingUnit);
                    _db.Ingredients.Add(ingredient);
                    await _db.SaveChangesAsync(ct);
                }

                recipe.AddIngredient(ingredient.Id, quantity);
            }
        }

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new
        {
            success = true,
            recipeId = recipe.Id,
            name = recipe.Name,
            ingredientCount = recipe.Ingredients.Count
        });
    }
}
