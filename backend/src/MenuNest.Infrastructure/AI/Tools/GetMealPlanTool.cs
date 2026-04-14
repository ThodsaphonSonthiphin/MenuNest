using System.Text.Json;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

// Adaptations:
//   - MealPlanEntry uses property MealSlot (not Slot).
//   - MealPlanEntry has no Recipe navigation — RecipeId only. Join with Recipes.
public sealed class GetMealPlanTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public GetMealPlanTool(IApplicationDbContext db) => _db = db;

    public string Name => "get_meal_plan";
    public string Description => "ดูแผนมื้ออาหาร ใช้เมื่อต้องการรู้ว่าวันไหนกินอะไร หรือเช็คว่ามีเมนูซ้ำไหม";
    public bool RequiresConfirmation => false;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "fromDate": { "type": "string", "format": "date", "description": "วันเริ่มต้น (YYYY-MM-DD) ถ้าไม่ระบุใช้วันนี้" },
            "toDate": { "type": "string", "format": "date", "description": "วันสิ้นสุด (YYYY-MM-DD) ถ้าไม่ระบุใช้ 7 วันข้างหน้า" }
        }
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct)
    {
        var from = arguments.TryGetProperty("fromDate", out var f) && f.GetString() is string fs
            ? DateOnly.Parse(fs)
            : DateOnly.FromDateTime(DateTime.UtcNow);

        var to = arguments.TryGetProperty("toDate", out var t) && t.GetString() is string ts
            ? DateOnly.Parse(ts)
            : from.AddDays(7);

        // Join MealPlanEntries with Recipes — no navigation property on MealPlanEntry.
        // Use method syntax to avoid conflict between variable name "from" and LINQ query keyword "from".
        var fromDate = from;
        var toDate = to;
        var entries = await _db.MealPlanEntries
            .Where(e => e.FamilyId == familyId && e.Date >= fromDate && e.Date <= toDate)
            .Join(_db.Recipes,
                e => e.RecipeId,
                r => r.Id,
                (e, r) => new
                {
                    date = e.Date.ToString("yyyy-MM-dd"),
                    slot = e.MealSlot.ToString(),
                    recipeName = r.Name,
                    recipeId = e.RecipeId,
                    sortDate = e.Date,
                    sortSlot = e.MealSlot
                })
            .OrderBy(x => x.sortDate)
            .ThenBy(x => x.sortSlot)
            .Select(x => new
            {
                x.date,
                x.slot,
                x.recipeName,
                x.recipeId
            })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { entries, count = entries.Count });
    }
}
