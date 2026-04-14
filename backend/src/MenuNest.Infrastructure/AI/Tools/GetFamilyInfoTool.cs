using System.Text.Json;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

// Note: Family.Members navigates to User collection via FamilyMembership.
// We use UserRelationships to count members.
public sealed class GetFamilyInfoTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public GetFamilyInfoTool(IApplicationDbContext db) => _db = db;

    public string Name => "get_family_info";
    public string Description => "ดูข้อมูลครอบครัว จำนวนสมาชิก ใช้เมื่อต้องการแนะนำปริมาณอาหารตามจำนวนคน";
    public bool RequiresConfirmation => false;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {}
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct)
    {
        var family = await _db.Families
            .Where(f => f.Id == familyId)
            .Select(f => new
            {
                name = f.Name,
                memberCount = f.Members.Count
            })
            .FirstOrDefaultAsync(ct);

        return JsonSerializer.Serialize(new { family });
    }
}
