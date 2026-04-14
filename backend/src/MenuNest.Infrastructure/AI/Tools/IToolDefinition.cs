using System.Text.Json;

namespace MenuNest.Infrastructure.AI.Tools;

public interface IToolDefinition
{
    string Name { get; }
    string Description { get; }
    bool RequiresConfirmation { get; }
    BinaryData ParametersSchema { get; }
    Task<string> ExecuteAsync(JsonElement arguments, Guid familyId, Guid userId, CancellationToken ct);
}
