using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Infrastructure.AI.Tools;

// ShoppingList.Create(familyId, name, createdByUserId) — matches actual signature.
public sealed class CreateShoppingListTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public CreateShoppingListTool(IApplicationDbContext db) => _db = db;

    public string Name => "create_shopping_list";
    public string Description => "สร้างรายการซื้อของใหม่";
    public bool RequiresConfirmation => true;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "name": { "type": "string", "description": "ชื่อรายการ เช่น 'ซื้อของวันจันทร์'" }
        },
        "required": ["name"]
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct)
    {
        var name = arguments.GetProperty("name").GetString()!;
        var list = ShoppingList.Create(familyId, name, userId);

        _db.ShoppingLists.Add(list);
        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new { success = true, shoppingListId = list.Id, name = list.Name });
    }
}
