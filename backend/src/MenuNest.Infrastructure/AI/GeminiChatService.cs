using System.Text.Json;
using System.Text.Json.Nodes;
using GenerativeAI;
using GenerativeAI.Types;
using MenuNest.Application.Abstractions;
using MenuNest.Infrastructure.AI.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiChatResponse = MenuNest.Application.Abstractions.AiChatResponse;
using DomainChatMessage = MenuNest.Domain.Entities.ChatMessage;

namespace MenuNest.Infrastructure.AI;

public sealed class GeminiChatService : IAiChatService
{
    private readonly GenerativeModel _model;
    private readonly IReadOnlyList<IToolDefinition> _tools;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<GeminiChatService> _logger;

    public GeminiChatService(
        IOptions<GeminiOptions> options,
        IEnumerable<IToolDefinition> tools,
        IApplicationDbContext db,
        ILogger<GeminiChatService> logger)
    {
        var opts = options.Value;
        _model = new GenerativeModel(opts.ApiKey, opts.Model);
        _tools = tools.ToList();
        _db = db;
        _logger = logger;
    }

    public async Task<AiChatResponse> ChatAsync(
        IReadOnlyList<DomainChatMessage> history,
        string userMessage,
        Guid familyId,
        Guid userId,
        CancellationToken ct)
    {
        var familyInfo = await _db.Families
            .Where(f => f.Id == familyId)
            .Select(f => new { f.Name, MemberCount = f.Members.Count })
            .FirstAsync(ct);

        var request = BuildRequest(history, userMessage, familyInfo.Name, familyInfo.MemberCount);
        return await RunToolLoopAsync(request, familyId, userId, ct);
    }

    public async Task<AiChatResponse> ExecutePendingActionsAsync(
        IReadOnlyList<DomainChatMessage> history,
        string pendingToolCallsJson,
        Guid familyId,
        Guid userId,
        CancellationToken ct)
    {
        var familyInfo = await _db.Families
            .Where(f => f.Id == familyId)
            .Select(f => new { f.Name, MemberCount = f.Members.Count })
            .FirstAsync(ct);

        // Rebuild the conversation up to the confirmation point (no new user message)
        var request = BuildRequest(history, null, familyInfo.Name, familyInfo.MemberCount);

        // Execute the pending tool calls
        var pendingCalls = JsonSerializer.Deserialize<List<PendingToolCall>>(pendingToolCallsJson)!;
        var toolResults = new List<string>();

        foreach (var call in pendingCalls)
        {
            var tool = _tools.FirstOrDefault(t => t.Name == call.Name);
            if (tool is null)
            {
                toolResults.Add($"Tool '{call.Name}' not found.");
                continue;
            }

            try
            {
                var argsElement = JsonSerializer.Deserialize<JsonElement>(call.Arguments);
                var result = await tool.ExecuteAsync(argsElement, familyId, userId, ct);
                toolResults.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tool {ToolName} execution failed", call.Name);
                toolResults.Add(JsonSerializer.Serialize(new { error = true, message = ex.Message }));
            }
        }

        // Ask AI to summarize what was done
        var summaryContext = string.Join("\n", pendingCalls.Zip(toolResults, (c, r) =>
            $"Tool: {c.Name}\nResult: {r}"));

        request.Contents ??= new List<Content>();
        request.Contents.Add(new Content(
            new[] { new Part { Text = $"ผู้ใช้ยืนยันแล้ว ผลลัพธ์ของการดำเนินการ:\n{summaryContext}\n\nกรุณาสรุปผลให้ผู้ใช้" } },
            "user"));

        // No tools needed for summary — create a plain request
        var summaryRequest = new GenerateContentRequest
        {
            Contents = request.Contents,
            SystemInstruction = request.SystemInstruction
        };

        var response = await _model.GenerateContentAsync(summaryRequest, ct);
        var text = response.Text() ?? "";

        return new AiChatResponse(text, null, null, false);
    }

    private async Task<AiChatResponse> RunToolLoopAsync(
        GenerateContentRequest request,
        Guid familyId,
        Guid userId,
        CancellationToken ct)
    {
        const int maxIterations = 10;

        for (var i = 0; i < maxIterations; i++)
        {
            var response = await _model.GenerateContentAsync(request, ct);
            var functionCalls = response.GetFunctions();

            // No function calls — final text response
            if (functionCalls is null || functionCalls.Count == 0)
            {
                var text = response.Text() ?? "";
                var structured = ExtractStructuredData(text);
                return new AiChatResponse(text, null, structured, false);
            }

            // Separate read tools from write tools (RequiresConfirmation)
            var pendingWrites = new List<PendingToolCall>();
            var readCalls = new List<FunctionCall>();

            foreach (var call in functionCalls)
            {
                var tool = _tools.FirstOrDefault(t => t.Name == call.Name);
                if (tool?.RequiresConfirmation == true)
                {
                    var argsJson = call.Args?.ToJsonString() ?? "{}";
                    pendingWrites.Add(new PendingToolCall(call.Name, argsJson));
                }
                else
                {
                    readCalls.Add(call);
                }
            }

            // Append the model's response (containing the function calls) to the conversation
            var modelContent = BuildModelFunctionCallContent(functionCalls);
            request.Contents!.Add(modelContent);

            // Execute read-only tools immediately and append their results
            foreach (var call in readCalls)
            {
                var result = await ExecuteFunctionCallAsync(call, familyId, userId, ct);
                var fr = new FunctionResponse
                {
                    Name = call.Name,
                    Response = JsonNode.Parse(result) ?? JsonNode.Parse("{}")!
                };
                request.Contents.Add(FunctionCallExtensions.ToFunctionCallContent(fr));
            }

            // If there are write tools that require confirmation, pause and ask user
            if (pendingWrites.Count > 0)
            {
                var pendingJson = JsonSerializer.Serialize(pendingWrites);

                var descriptions = pendingWrites.Select(p =>
                {
                    var tool = _tools.First(t => t.Name == p.Name);
                    return $"- {tool.Description}: {p.Arguments}";
                });

                // Ask AI to present the confirmation to the user
                request.Contents.Add(new Content(
                    new[] { new Part { Text = $"[SYSTEM] ต้องยืนยันก่อนดำเนินการต่อไปนี้:\n{string.Join("\n", descriptions)}\n\nกรุณาสรุปให้ผู้ใช้รู้ว่าจะทำอะไรบ้าง แล้วถามยืนยัน" } },
                    "user"));

                var confirmRequest = new GenerateContentRequest
                {
                    Contents = request.Contents,
                    SystemInstruction = request.SystemInstruction
                };

                var confirmResponse = await _model.GenerateContentAsync(confirmRequest, ct);
                var confirmText = confirmResponse.Text() ?? "";
                var confirmStructured = JsonSerializer.Serialize(new
                {
                    type = "confirmation",
                    actions = pendingWrites.Select(p => new { tool = p.Name, argsJson = p.Arguments })
                });

                return new AiChatResponse(confirmText, pendingJson, confirmStructured, true);
            }
        }

        return new AiChatResponse("ขออภัยค่ะ ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง", null, null, false);
    }

    private async Task<string> ExecuteFunctionCallAsync(
        FunctionCall call,
        Guid familyId,
        Guid userId,
        CancellationToken ct)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null)
            return JsonSerializer.Serialize(new { error = $"Unknown tool: {call.Name}" });

        try
        {
            var argsJson = call.Args?.ToJsonString() ?? "{}";
            var argsElement = JsonSerializer.Deserialize<JsonElement>(argsJson);
            return await tool.ExecuteAsync(argsElement, familyId, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {Tool} failed", call.Name);
            return JsonSerializer.Serialize(new { error = true, message = ex.Message });
        }
    }

    /// <summary>
    /// Builds a Content object representing the model's turn that contains function calls.
    /// </summary>
    private static Content BuildModelFunctionCallContent(List<FunctionCall> calls)
    {
        var parts = calls.Select(c => new Part { FunctionCall = c }).ToArray();
        return new Content(parts, "model");
    }

    private GenerateContentRequest BuildRequest(
        IReadOnlyList<DomainChatMessage> history,
        string? newUserMessage,
        string familyName,
        int memberCount)
    {
        var systemText = ChatSystemPrompt.Build(familyName, memberCount);

        var contents = new List<Content>();

        // Add history (limit to last 40 messages to stay within token budget)
        var recent = history.TakeLast(40);
        foreach (var msg in recent)
        {
            switch (msg.Role)
            {
                case Domain.Enums.ChatRole.User:
                    contents.Add(new Content(new[] { new Part { Text = msg.Content } }, "user"));
                    break;
                case Domain.Enums.ChatRole.Assistant:
                    contents.Add(new Content(new[] { new Part { Text = msg.Content } }, "model"));
                    break;
                case Domain.Enums.ChatRole.Tool:
                    // Tool messages in history are informational, skip in replay
                    break;
            }
        }

        if (newUserMessage is not null)
            contents.Add(new Content(new[] { new Part { Text = newUserMessage } }, "user"));

        var tools = BuildTools();

        return new GenerateContentRequest
        {
            SystemInstruction = new Content(new[] { new Part { Text = systemText } }, "user"),
            Contents = contents,
            Tools = tools
        };
    }

    private List<Tool> BuildTools()
    {
        var declarations = _tools.Select(t =>
        {
            var schemaJson = t.ParametersSchema.ToString();
            var parameters = JsonSerializer.Deserialize<Schema>(schemaJson);

            return new FunctionDeclaration
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = parameters
            };
        }).ToList();

        return new List<Tool>
        {
            new Tool { FunctionDeclarations = declarations }
        };
    }

    private static string? ExtractStructuredData(string text)
    {
        // Extract JSON blocks from markdown code fences in the AI response
        var startIdx = text.IndexOf("```json");
        if (startIdx < 0) return null;

        startIdx = text.IndexOf('\n', startIdx) + 1;
        var endIdx = text.IndexOf("```", startIdx);
        if (endIdx < 0) return null;

        var json = text[startIdx..endIdx].Trim();
        try
        {
            JsonSerializer.Deserialize<JsonElement>(json); // validate
            return json;
        }
        catch
        {
            return null;
        }
    }

    private sealed record PendingToolCall(string Name, string Arguments);
}
