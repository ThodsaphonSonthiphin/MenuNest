using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.AI.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using AiChatResponse = MenuNest.Application.Abstractions.AiChatResponse;
using DomainChatMessage = MenuNest.Domain.Entities.ChatMessage;

namespace MenuNest.Infrastructure.AI;

public sealed class AzureOpenAiChatService : IAiChatService
{
    private readonly ChatClient _chatClient;
    private readonly IReadOnlyList<IToolDefinition> _tools;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<AzureOpenAiChatService> _logger;

    public AzureOpenAiChatService(
        IOptions<AzureOpenAiOptions> options,
        IEnumerable<IToolDefinition> tools,
        IApplicationDbContext db,
        ILogger<AzureOpenAiChatService> logger)
    {
        var opts = options.Value;
        var client = new AzureOpenAIClient(new Uri(opts.Endpoint), new ApiKeyCredential(opts.ApiKey));
        _chatClient = client.GetChatClient(opts.DeploymentName);
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

        var messages = BuildMessages(history, userMessage, familyInfo.Name, familyInfo.MemberCount);
        var options = BuildChatOptions();

        return await RunToolLoopAsync(messages, options, familyId, userId, ct);
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

        // Rebuild the conversation up to the confirmation point
        var messages = BuildMessages(history, null, familyInfo.Name, familyInfo.MemberCount);

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
                var args = JsonSerializer.Deserialize<JsonElement>(call.Arguments);
                var result = await tool.ExecuteAsync(args, familyId, userId, ct);
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

        messages.Add(new UserChatMessage(
            $"ผู้ใช้ยืนยันแล้ว ผลลัพธ์ของการดำเนินการ:\n{summaryContext}\n\nกรุณาสรุปผลให้ผู้ใช้"));

        var opts = new ChatCompletionOptions(); // No tools needed for summary
        var response = await _chatClient.CompleteChatAsync(messages, opts, ct);

        return new AiChatResponse(
            Content: response.Value.Content[0].Text,
            ToolCallsJson: null,
            StructuredDataJson: null,
            HasPendingWriteActions: false);
    }

    private async Task<AiChatResponse> RunToolLoopAsync(
        List<OpenAI.Chat.ChatMessage> messages,
        ChatCompletionOptions options,
        Guid familyId,
        Guid userId,
        CancellationToken ct)
    {
        const int maxIterations = 10;

        for (var i = 0; i < maxIterations; i++)
        {
            var response = await _chatClient.CompleteChatAsync(messages, options, ct);
            var completion = response.Value;

            // Final text response
            if (completion.FinishReason != ChatFinishReason.ToolCalls)
            {
                var text = completion.Content.Count > 0 ? completion.Content[0].Text : "";
                var structured = ExtractStructuredData(text);
                return new AiChatResponse(text, null, structured, false);
            }

            // Check if any tool calls require confirmation
            var pendingWrites = new List<PendingToolCall>();
            var readToolCalls = new List<ChatToolCall>();

            foreach (var toolCall in completion.ToolCalls)
            {
                var tool = _tools.FirstOrDefault(t => t.Name == toolCall.FunctionName);
                if (tool?.RequiresConfirmation == true)
                {
                    pendingWrites.Add(new PendingToolCall(toolCall.FunctionName, toolCall.FunctionArguments.ToString()));
                }
                else
                {
                    readToolCalls.Add(toolCall);
                }
            }

            // If there are write tools, pause and return confirmation request
            if (pendingWrites.Count > 0)
            {
                // Execute only the read tools first
                var assistantMessage = new AssistantChatMessage(completion);
                messages.Add(assistantMessage);

                foreach (var readCall in readToolCalls)
                {
                    var result = await ExecuteToolCallAsync(readCall, familyId, userId, ct);
                    messages.Add(new ToolChatMessage(readCall.Id, result));
                }

                var pendingJson = JsonSerializer.Serialize(pendingWrites);

                // Build a description of pending actions for the AI to present
                var descriptions = pendingWrites.Select(p =>
                {
                    var tool = _tools.First(t => t.Name == p.Name);
                    return $"- {tool.Description}: {p.Arguments}";
                });

                // Ask AI to present the confirmation to user
                messages.Add(new UserChatMessage(
                    $"[SYSTEM] ต้องยืนยันก่อนดำเนินการต่อไปนี้:\n{string.Join("\n", descriptions)}\n\nกรุณาสรุปให้ผู้ใช้รู้ว่าจะทำอะไรบ้าง แล้วถามยืนยัน"));

                var confirmResponse = await _chatClient.CompleteChatAsync(messages, new ChatCompletionOptions(), ct);
                var confirmText = confirmResponse.Value.Content[0].Text;
                var confirmStructured = JsonSerializer.Serialize(new
                {
                    type = "confirmation",
                    actions = pendingWrites.Select(p => new { tool = p.Name, argsJson = p.Arguments })
                });

                return new AiChatResponse(confirmText, pendingJson, confirmStructured, true);
            }

            // All tools are read-only — execute them and continue the loop
            var assistantMsg = new AssistantChatMessage(completion);
            messages.Add(assistantMsg);

            foreach (var toolCall in completion.ToolCalls)
            {
                var result = await ExecuteToolCallAsync(toolCall, familyId, userId, ct);
                messages.Add(new ToolChatMessage(toolCall.Id, result));
            }
        }

        return new AiChatResponse("ขออภัยค่ะ ดำเนินการไม่สำเร็จ กรุณาลองใหม่อีกครั้ง", null, null, false);
    }

    private async Task<string> ExecuteToolCallAsync(
        ChatToolCall toolCall,
        Guid familyId,
        Guid userId,
        CancellationToken ct)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == toolCall.FunctionName);
        if (tool is null)
            return JsonSerializer.Serialize(new { error = $"Unknown tool: {toolCall.FunctionName}" });

        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(toolCall.FunctionArguments.ToString());
            return await tool.ExecuteAsync(args, familyId, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {Tool} failed", toolCall.FunctionName);
            return JsonSerializer.Serialize(new { error = true, message = ex.Message });
        }
    }

    private List<OpenAI.Chat.ChatMessage> BuildMessages(
        IReadOnlyList<DomainChatMessage> history,
        string? newUserMessage,
        string familyName,
        int memberCount)
    {
        var messages = new List<OpenAI.Chat.ChatMessage>
        {
            new SystemChatMessage(ChatSystemPrompt.Build(familyName, memberCount))
        };

        // Add history (limit to last 40 messages to stay within token budget)
        var recent = history.TakeLast(40);
        foreach (var msg in recent)
        {
            switch (msg.Role)
            {
                case Domain.Enums.ChatRole.User:
                    messages.Add(new UserChatMessage(msg.Content));
                    break;
                case Domain.Enums.ChatRole.Assistant:
                    messages.Add(new AssistantChatMessage(msg.Content));
                    break;
                case Domain.Enums.ChatRole.Tool:
                    // Tool messages in history are informational, skip in replay
                    break;
            }
        }

        if (newUserMessage is not null)
            messages.Add(new UserChatMessage(newUserMessage));

        return messages;
    }

    private ChatCompletionOptions BuildChatOptions()
    {
        var options = new ChatCompletionOptions();

        foreach (var tool in _tools)
        {
            options.Tools.Add(ChatTool.CreateFunctionTool(
                tool.Name,
                tool.Description,
                tool.ParametersSchema));
        }

        return options;
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
