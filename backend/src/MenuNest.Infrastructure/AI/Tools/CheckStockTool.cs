using System.Text.Json;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

// Adaptation: StockItem has no Ingredient navigation property — only IngredientId.
// We join StockItems with Ingredients in-query using a projection.
public sealed class CheckStockTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public CheckStockTool(IApplicationDbContext db) => _db = db;

    public string Name => "check_stock";
    public string Description => "ตรวจสอบวัตถุดิบที่มีในสต็อก ใช้เมื่อต้องการดูว่ามีของอะไรในครัว หรือเช็คว่าทำเมนูไหนได้";
    public bool RequiresConfirmation => false;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "ingredientNames": {
                "type": "array",
                "items": { "type": "string" },
                "description": "รายชื่อวัตถุดิบที่ต้องการเช็ค ถ้าไม่ระบุจะแสดงสต็อกทั้งหมด"
            }
        }
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct)
    {
        List<string> nameFilter = [];

        if (arguments.TryGetProperty("ingredientNames", out var names) && names.GetArrayLength() > 0)
        {
            nameFilter = names.EnumerateArray().Select(n => n.GetString()!).ToList();
        }

        // Join StockItems with Ingredients — StockItem has no navigation, join via IngredientId.
        var stockQuery =
            from s in _db.StockItems
            join i in _db.Ingredients on s.IngredientId equals i.Id
            where s.FamilyId == familyId && s.Quantity > 0
            select new { name = i.Name, unit = i.Unit, quantity = s.Quantity };

        if (nameFilter.Count > 0)
        {
            stockQuery = stockQuery.Where(x => nameFilter.Any(n => EF.Functions.Like(x.name, $"%{n}%")));
        }

        var items = await stockQuery
            .Take(50)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { stockItems = items, count = items.Count });
    }
}
