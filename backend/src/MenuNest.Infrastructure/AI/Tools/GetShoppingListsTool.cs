using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

// Adaptation: ShoppingList uses ShoppingListStatus enum, not bool IsCompleted.
// Use Status == ShoppingListStatus.Completed for the isCompleted flag.
public sealed class GetShoppingListsTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public GetShoppingListsTool(IApplicationDbContext db) => _db = db;

    public string Name => "get_shopping_lists";
    public string Description => "ดูรายการ shopping lists ที่มีอยู่ ใช้เมื่อต้องการเช็คว่ามีรายการซื้อของอะไรบ้าง";
    public bool RequiresConfirmation => false;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {}
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct)
    {
        var lists = await _db.ShoppingLists
            .Where(l => l.FamilyId == familyId)
            .Select(l => new
            {
                id = l.Id,
                name = l.Name,
                status = l.Status.ToString(),
                itemCount = l.Items.Count,
                boughtCount = l.Items.Count(i => i.IsBought),
                isCompleted = l.Status == ShoppingListStatus.Completed
            })
            .OrderByDescending(l => l.id)
            .Take(10)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { shoppingLists = lists });
    }
}
