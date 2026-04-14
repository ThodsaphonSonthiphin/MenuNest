using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

// Adaptation: MealPlanEntry.Create signature is Create(familyId, date, slot, recipeId, createdByUserId, notes).
// Plan had arguments in wrong order: Create(familyId, recipeId, date, slot, userId).
public sealed class AddToMealPlanTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public AddToMealPlanTool(IApplicationDbContext db) => _db = db;

    public string Name => "add_to_meal_plan";
    public string Description => "เพิ่มเมนูเข้าแผนมื้ออาหาร";
    public bool RequiresConfirmation => true;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "recipeId": { "type": "string", "format": "uuid", "description": "ID ของสูตรอาหาร" },
            "date": { "type": "string", "format": "date", "description": "วันที่ (YYYY-MM-DD)" },
            "slot": { "type": "string", "enum": ["Breakfast", "Lunch", "Dinner"], "description": "มื้อ" }
        },
        "required": ["recipeId", "date", "slot"]
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct)
    {
        var recipeId = Guid.Parse(arguments.GetProperty("recipeId").GetString()!);
        var date = DateOnly.Parse(arguments.GetProperty("date").GetString()!);
        var slotStr = arguments.GetProperty("slot").GetString()!;

        if (!Enum.TryParse<Domain.Enums.MealSlot>(slotStr, true, out var slot))
            throw new DomainException($"Invalid meal slot: {slotStr}");

        var recipe = await _db.Recipes
            .FirstOrDefaultAsync(r => r.Id == recipeId && r.FamilyId == familyId, ct)
            ?? throw new DomainException("Recipe not found.");

        // Actual signature: Create(familyId, date, slot, recipeId, createdByUserId, notes)
        var entry = MealPlanEntry.Create(familyId, date, slot, recipeId, userId);
        _db.MealPlanEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new
        {
            success = true,
            date = date.ToString("yyyy-MM-dd"),
            slot = slot.ToString(),
            recipeName = recipe.Name
        });
    }
}
