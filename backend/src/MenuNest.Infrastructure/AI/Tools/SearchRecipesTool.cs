using System.Text.Json;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

public sealed class SearchRecipesTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public SearchRecipesTool(IApplicationDbContext db) => _db = db;

    public string Name => "search_recipes";
    public string Description => "ค้นหาสูตรอาหารจากชื่อหรือวัตถุดิบ ใช้เมื่อต้องการหาเมนูที่มีในระบบ";
    public bool RequiresConfirmation => false;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "query": { "type": "string", "description": "ชื่อเมนูหรือวัตถุดิบที่ต้องการค้นหา" }
        },
        "required": ["query"]
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct)
    {
        var query = arguments.GetProperty("query").GetString() ?? "";

        var recipes = await _db.Recipes
            .Where(r => r.FamilyId == familyId)
            .Where(r => EF.Functions.Like(r.Name, $"%{query}%"))
            .Include(r => r.Ingredients)
            .OrderBy(r => r.Name)
            .Take(10)
            .ToListAsync(ct);

        if (recipes.Count == 0)
            return JsonSerializer.Serialize(new { found = false, message = $"ไม่พบสูตรที่ตรงกับ '{query}'" });

        var results = recipes.Select(r => new
        {
            recipeId = r.Id,
            name = r.Name,
            description = r.Description,
            imageBlobPath = r.ImageBlobPath,
            ingredientCount = r.Ingredients.Count
        });

        return JsonSerializer.Serialize(new { found = true, recipes = results });
    }
}
