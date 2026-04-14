# AI Assistant Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an AI-powered food consultant to MenuNest that can answer questions, recommend meals, and execute actions (add to meal plan, create shopping lists, create recipes) via text or voice input.

**Architecture:** Azure OpenAI with function calling (tool use) for the LLM, Azure AI Speech SDK for client-side voice input, Syncfusion Chat UI for the frontend. Backend tools reuse existing CQRS handlers through Mediator. All write operations require user confirmation before execution.

**Tech Stack:** Azure OpenAI (GPT-4o), Azure AI Speech (th-TH), Syncfusion react-interactive-chat, ASP.NET Core 10, EF Core 10, RTK Query

**Spec:** `docs/superpowers/specs/2026-04-14-ai-assistant-design.md`

---

## File Structure

### Backend — New Files

```
backend/src/MenuNest.Domain/
  Entities/ChatConversation.cs          — conversation aggregate
  Entities/ChatMessage.cs               — message entity
  Enums/ChatRole.cs                     — User/Assistant/Tool enum

backend/src/MenuNest.Infrastructure/
  Persistence/Configurations/
    ChatConversationConfiguration.cs    — EF config + indexes
    ChatMessageConfiguration.cs         — EF config + indexes
  AI/
    IAiChatService.cs                   — interface (lives here, not Application, because it depends on Azure SDK types)
    AzureOpenAiChatService.cs           — orchestrate tool calling loop
    AzureOpenAiOptions.cs               — config POCO
    ChatSystemPrompt.cs                 — system prompt builder
    Tools/
      IToolDefinition.cs               — tool interface
      SearchRecipesTool.cs             — read: search recipes
      CheckStockTool.cs                — read: check stock
      GetMealPlanTool.cs               — read: get meal plan
      GetShoppingListsTool.cs          — read: get shopping lists
      GetFamilyInfoTool.cs             — read: get family info
      CreateRecipeTool.cs              — write: create recipe
      AddToMealPlanTool.cs             — write: add to meal plan
      CreateShoppingListTool.cs        — write: create shopping list
      AddShoppingItemsTool.cs          — write: add items to list

backend/src/MenuNest.Application/
  UseCases/Chat/
    ChatDtos.cs                         — all chat DTOs
    CreateConversation/
      CreateConversationCommand.cs
      CreateConversationHandler.cs
    ListConversations/
      ListConversationsQuery.cs
      ListConversationsHandler.cs
    GetMessages/
      GetMessagesQuery.cs
      GetMessagesHandler.cs
    SendMessage/
      SendMessageCommand.cs
      SendMessageHandler.cs
    DeleteConversation/
      DeleteConversationCommand.cs
      DeleteConversationHandler.cs
    GetSpeechToken/
      GetSpeechTokenQuery.cs
      GetSpeechTokenHandler.cs

backend/src/MenuNest.WebApi/
  Controllers/ChatController.cs         — 6 endpoints
```

### Backend — Modified Files

```
backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs          — add DbSets
backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs   — add DbSets
backend/src/MenuNest.Infrastructure/DependencyInjection.cs              — register AI services
backend/src/MenuNest.WebApi/MenuNest.WebApi.csproj                      — add Azure.AI.OpenAI
backend/src/MenuNest.Infrastructure/MenuNest.Infrastructure.csproj      — add Azure.AI.OpenAI
```

### Frontend — New Files

```
frontend/src/pages/ai-assistant/
  AiAssistantPage.tsx                   — main page
  aiAssistantSlice.ts                   — Redux slice
  hooks/
    useAiAssistant.ts                   — chat logic hook
    useAzureSpeech.ts                   — speech SDK hook
  components/
    RecipeCard.tsx                      — recipe card in chat
    ConfirmationMessage.tsx             — confirm/reject UI
```

### Frontend — Modified Files

```
frontend/src/shared/api/api.ts          — add chat endpoints
frontend/src/store/index.ts             — add aiAssistant slice
frontend/src/router.tsx                 — add /ai-assistant route
frontend/src/shared/components/NavBar.tsx — add nav item
frontend/src/main.tsx                   — add interactive-chat CSS import
frontend/package.json                   — add 2 packages
```

---

## Task 1: Domain Entities

**Files:**
- Create: `backend/src/MenuNest.Domain/Enums/ChatRole.cs`
- Create: `backend/src/MenuNest.Domain/Entities/ChatConversation.cs`
- Create: `backend/src/MenuNest.Domain/Entities/ChatMessage.cs`

- [ ] **Step 1: Create ChatRole enum**

```csharp
// backend/src/MenuNest.Domain/Enums/ChatRole.cs
namespace MenuNest.Domain.Enums;

public enum ChatRole
{
    User,
    Assistant,
    Tool
}
```

- [ ] **Step 2: Create ChatMessage entity**

```csharp
// backend/src/MenuNest.Domain/Entities/ChatMessage.cs
using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;

namespace MenuNest.Domain.Entities;

public sealed class ChatMessage : Entity
{
    public Guid ConversationId { get; private set; }
    public ChatRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? ToolCalls { get; private set; }
    public string? ToolName { get; private set; }
    public string? StructuredData { get; private set; }

    private ChatMessage() { }

    public static ChatMessage CreateUserMessage(Guid conversationId, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new Exceptions.DomainException("Message content cannot be empty.");

        return new ChatMessage
        {
            ConversationId = conversationId,
            Role = ChatRole.User,
            Content = content
        };
    }

    public static ChatMessage CreateAssistantMessage(
        Guid conversationId,
        string content,
        string? toolCalls = null,
        string? structuredData = null)
    {
        return new ChatMessage
        {
            ConversationId = conversationId,
            Role = ChatRole.Assistant,
            Content = content,
            ToolCalls = toolCalls,
            StructuredData = structuredData
        };
    }

    public static ChatMessage CreateToolMessage(
        Guid conversationId,
        string toolName,
        string content)
    {
        return new ChatMessage
        {
            ConversationId = conversationId,
            Role = ChatRole.Tool,
            ToolName = toolName,
            Content = content
        };
    }
}
```

- [ ] **Step 3: Create ChatConversation entity**

```csharp
// backend/src/MenuNest.Domain/Entities/ChatConversation.cs
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

public sealed class ChatConversation : Entity
{
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string Title { get; private set; } = string.Empty;

    private readonly List<ChatMessage> _messages = new();
    public IReadOnlyCollection<ChatMessage> Messages => _messages.AsReadOnly();

    private ChatConversation() { }

    public static ChatConversation Create(Guid userId, Guid familyId, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Conversation title cannot be empty.");

        return new ChatConversation
        {
            UserId = userId,
            FamilyId = familyId,
            Title = title.Length > 100 ? title[..100] : title
        };
    }

    public void UpdateTitle(string title)
    {
        Title = title.Length > 100 ? title[..100] : title;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.Domain/Enums/ChatRole.cs backend/src/MenuNest.Domain/Entities/ChatConversation.cs backend/src/MenuNest.Domain/Entities/ChatMessage.cs
git commit -m "feat(ai): add ChatConversation, ChatMessage entities and ChatRole enum"
```

---

## Task 2: EF Core Configuration + DbContext

**Files:**
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/ChatConversationConfiguration.cs`
- Create: `backend/src/MenuNest.Infrastructure/Persistence/Configurations/ChatMessageConfiguration.cs`
- Modify: `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs`

- [ ] **Step 1: Create ChatConversationConfiguration**

```csharp
// backend/src/MenuNest.Infrastructure/Persistence/Configurations/ChatConversationConfiguration.cs
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("ChatConversations");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.UserId).IsRequired();
        builder.Property(c => c.FamilyId).IsRequired();
        builder.Property(c => c.Title).IsRequired().HasMaxLength(100);

        builder.HasIndex(c => new { c.UserId, c.FamilyId, c.UpdatedAt })
            .IsDescending(false, false, true);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(c => c.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(ChatConversation.Messages))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

- [ ] **Step 2: Create ChatMessageConfiguration**

```csharp
// backend/src/MenuNest.Infrastructure/Persistence/Configurations/ChatMessageConfiguration.cs
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MenuNest.Infrastructure.Persistence.Configurations;

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.ConversationId).IsRequired();
        builder.Property(m => m.Role).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.ToolCalls).HasColumnType("nvarchar(max)");
        builder.Property(m => m.ToolName).HasMaxLength(100);
        builder.Property(m => m.StructuredData).HasColumnType("nvarchar(max)");

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}
```

- [ ] **Step 3: Add DbSets to IApplicationDbContext**

Add to the existing interface in `backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs`:

```csharp
DbSet<ChatConversation> ChatConversations { get; }
DbSet<ChatMessage> ChatMessages { get; }
```

- [ ] **Step 4: Add DbSets to AppDbContext**

Add to the existing class in `backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs`:

```csharp
public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
```

- [ ] **Step 5: Create EF migration**

```bash
cd backend
dotnet ef migrations add AddChatEntities --project src/MenuNest.Infrastructure --startup-project src/MenuNest.WebApi
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/Persistence/Configurations/ChatConversationConfiguration.cs backend/src/MenuNest.Infrastructure/Persistence/Configurations/ChatMessageConfiguration.cs backend/src/MenuNest.Application/Abstractions/IApplicationDbContext.cs backend/src/MenuNest.Infrastructure/Persistence/AppDbContext.cs backend/src/MenuNest.Infrastructure/Persistence/Migrations/
git commit -m "feat(ai): add EF Core config and migration for chat entities"
```

---

## Task 3: Chat DTOs + Application Abstractions

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Chat/ChatDtos.cs`

- [ ] **Step 1: Create all chat DTOs**

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/ChatDtos.cs
namespace MenuNest.Application.UseCases.Chat;

// --- Response DTOs ---

public sealed record ConversationSummaryDto(
    Guid Id,
    string Title,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record ChatMessageDto(
    Guid Id,
    string Role,
    string Content,
    string? StructuredData,
    DateTime CreatedAt);

public sealed record SendMessageResponseDto(
    Guid MessageId,
    string Role,
    string Content,
    string? StructuredData,
    DateTime CreatedAt);

public sealed record SpeechTokenDto(
    string Token,
    string Region);

// --- Structured Data Types (serialized to JSON in StructuredData column) ---

public sealed record AiStructuredResponse(
    string Type,
    List<AiRecipeCard>? Cards = null,
    List<AiPendingAction>? Actions = null);

public sealed record AiRecipeCard(
    Guid RecipeId,
    string Name,
    string? ImageBlobPath,
    string? StockMatch);

public sealed record AiPendingAction(
    string Tool,
    string Description,
    string ArgsJson);
```

- [ ] **Step 2: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Chat/ChatDtos.cs
git commit -m "feat(ai): add chat DTOs"
```

---

## Task 4: CQRS Handlers — Conversation CRUD

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Chat/CreateConversation/CreateConversationCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/CreateConversation/CreateConversationHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/ListConversations/ListConversationsQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/ListConversations/ListConversationsHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/GetMessages/GetMessagesQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/GetMessages/GetMessagesHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/DeleteConversation/DeleteConversationCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/DeleteConversation/DeleteConversationHandler.cs`

- [ ] **Step 1: Create CreateConversationCommand + Handler**

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/CreateConversation/CreateConversationCommand.cs
using Mediator;

namespace MenuNest.Application.UseCases.Chat.CreateConversation;

public sealed record CreateConversationCommand : ICommand<ConversationSummaryDto>;
```

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/CreateConversation/CreateConversationHandler.cs
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Application.UseCases.Chat.CreateConversation;

public sealed class CreateConversationHandler : ICommandHandler<CreateConversationCommand, ConversationSummaryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public CreateConversationHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<ConversationSummaryDto> Handle(
        CreateConversationCommand command,
        CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var conversation = ChatConversation.Create(user.Id, familyId, "บทสนทนาใหม่");

        _db.ChatConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);

        return new ConversationSummaryDto(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAt,
            conversation.UpdatedAt);
    }
}
```

- [ ] **Step 2: Create ListConversationsQuery + Handler**

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/ListConversations/ListConversationsQuery.cs
using Mediator;

namespace MenuNest.Application.UseCases.Chat.ListConversations;

public sealed record ListConversationsQuery : IQuery<IReadOnlyList<ConversationSummaryDto>>;
```

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/ListConversations/ListConversationsHandler.cs
using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Chat.ListConversations;

public sealed class ListConversationsHandler : IQueryHandler<ListConversationsQuery, IReadOnlyList<ConversationSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public ListConversationsHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<ConversationSummaryDto>> Handle(
        ListConversationsQuery query,
        CancellationToken ct)
    {
        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        return await _db.ChatConversations
            .Where(c => c.UserId == user.Id && c.FamilyId == familyId)
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Select(c => new ConversationSummaryDto(
                c.Id, c.Title, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 3: Create GetMessagesQuery + Handler**

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/GetMessages/GetMessagesQuery.cs
using Mediator;

namespace MenuNest.Application.UseCases.Chat.GetMessages;

public sealed record GetMessagesQuery(Guid ConversationId) : IQuery<IReadOnlyList<ChatMessageDto>>;
```

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/GetMessages/GetMessagesHandler.cs
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Chat.GetMessages;

public sealed class GetMessagesHandler : IQueryHandler<GetMessagesQuery, IReadOnlyList<ChatMessageDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetMessagesHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<IReadOnlyList<ChatMessageDto>> Handle(
        GetMessagesQuery query,
        CancellationToken ct)
    {
        var (user, _) = await _userProvisioner.RequireFamilyAsync(ct);

        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == query.ConversationId && c.UserId == user.Id, ct)
            ?? throw new DomainException("Conversation not found.");

        return await _db.ChatMessages
            .Where(m => m.ConversationId == conversation.Id && m.Role != Domain.Enums.ChatRole.Tool)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageDto(
                m.Id,
                m.Role.ToString(),
                m.Content,
                m.StructuredData,
                m.CreatedAt))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 4: Create DeleteConversationCommand + Handler**

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/DeleteConversation/DeleteConversationCommand.cs
using Mediator;

namespace MenuNest.Application.UseCases.Chat.DeleteConversation;

public sealed record DeleteConversationCommand(Guid Id) : ICommand;
```

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/DeleteConversation/DeleteConversationHandler.cs
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Chat.DeleteConversation;

public sealed class DeleteConversationHandler : ICommandHandler<DeleteConversationCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public DeleteConversationHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<Unit> Handle(
        DeleteConversationCommand command,
        CancellationToken ct)
    {
        var (user, _) = await _userProvisioner.RequireFamilyAsync(ct);

        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == command.Id && c.UserId == user.Id, ct)
            ?? throw new DomainException("Conversation not found.");

        _db.ChatConversations.Remove(conversation);
        await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
```

- [ ] **Step 5: Verify build**

```bash
cd backend && dotnet build
```

Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Chat/
git commit -m "feat(ai): add conversation CRUD handlers"
```

---

## Task 5: AI Tool Definitions

**Files:**
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/IToolDefinition.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/SearchRecipesTool.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/CheckStockTool.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/GetMealPlanTool.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/GetShoppingListsTool.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/GetFamilyInfoTool.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/CreateRecipeTool.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/AddToMealPlanTool.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/CreateShoppingListTool.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/Tools/AddShoppingItemsTool.cs`

- [ ] **Step 1: Create IToolDefinition interface**

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/IToolDefinition.cs
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
```

- [ ] **Step 2: Create read tools — SearchRecipesTool**

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/SearchRecipesTool.cs
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

    public async Task<string> ExecuteAsync(JsonElement args, Guid familyId, Guid userId, CancellationToken ct)
    {
        var query = args.GetProperty("query").GetString() ?? "";

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
```

- [ ] **Step 3: Create read tools — CheckStockTool**

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/CheckStockTool.cs
using System.Text.Json;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

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

    public async Task<string> ExecuteAsync(JsonElement args, Guid familyId, Guid userId, CancellationToken ct)
    {
        var query = _db.StockItems
            .Where(s => s.FamilyId == familyId && s.Quantity > 0);

        if (args.TryGetProperty("ingredientNames", out var names) && names.GetArrayLength() > 0)
        {
            var nameList = names.EnumerateArray().Select(n => n.GetString()!).ToList();
            query = query.Where(s => nameList.Any(n => EF.Functions.Like(s.Ingredient.Name, $"%{n}%")));
        }

        var items = await query
            .Select(s => new { name = s.Ingredient.Name, unit = s.Ingredient.Unit, quantity = s.Quantity })
            .Take(50)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { stockItems = items, count = items.Count });
    }
}
```

- [ ] **Step 4: Create read tools — GetMealPlanTool**

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/GetMealPlanTool.cs
using System.Text.Json;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

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

    public async Task<string> ExecuteAsync(JsonElement args, Guid familyId, Guid userId, CancellationToken ct)
    {
        var from = args.TryGetProperty("fromDate", out var f) && f.GetString() is string fs
            ? DateOnly.Parse(fs)
            : DateOnly.FromDateTime(DateTime.UtcNow);

        var to = args.TryGetProperty("toDate", out var t) && t.GetString() is string ts
            ? DateOnly.Parse(ts)
            : from.AddDays(7);

        var entries = await _db.MealPlanEntries
            .Where(e => e.FamilyId == familyId && e.Date >= from && e.Date <= to)
            .Select(e => new
            {
                date = e.Date.ToString("yyyy-MM-dd"),
                slot = e.Slot.ToString(),
                recipeName = e.Recipe.Name,
                recipeId = e.RecipeId
            })
            .OrderBy(e => e.date).ThenBy(e => e.slot)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { entries, count = entries.Count });
    }
}
```

- [ ] **Step 5: Create read tools — GetShoppingListsTool**

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/GetShoppingListsTool.cs
using System.Text.Json;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

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

    public async Task<string> ExecuteAsync(JsonElement args, Guid familyId, Guid userId, CancellationToken ct)
    {
        var lists = await _db.ShoppingLists
            .Where(l => l.FamilyId == familyId)
            .Select(l => new
            {
                id = l.Id,
                name = l.Name,
                itemCount = l.Items.Count,
                boughtCount = l.Items.Count(i => i.IsBought),
                isCompleted = l.IsCompleted
            })
            .OrderByDescending(l => l.id)
            .Take(10)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { shoppingLists = lists });
    }
}
```

- [ ] **Step 6: Create read tools — GetFamilyInfoTool**

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/GetFamilyInfoTool.cs
using System.Text.Json;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

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

    public async Task<string> ExecuteAsync(JsonElement args, Guid familyId, Guid userId, CancellationToken ct)
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
```

- [ ] **Step 7: Create write tools — CreateRecipeTool**

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/CreateRecipeTool.cs
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

public sealed class CreateRecipeTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public CreateRecipeTool(IApplicationDbContext db) => _db = db;

    public string Name => "create_recipe";
    public string Description => "สร้างสูตรอาหารใหม่พร้อมวัตถุดิบ";
    public bool RequiresConfirmation => true;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "name": { "type": "string", "description": "ชื่อเมนู" },
            "description": { "type": "string", "description": "คำอธิบายสั้นๆ" },
            "ingredients": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "unit": { "type": "string" },
                        "quantity": { "type": "number" }
                    },
                    "required": ["name", "unit", "quantity"]
                },
                "description": "รายการวัตถุดิบ"
            }
        },
        "required": ["name", "ingredients"]
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement args, Guid familyId, Guid userId, CancellationToken ct)
    {
        var name = args.GetProperty("name").GetString()!;
        var description = args.TryGetProperty("description", out var desc) ? desc.GetString() : null;

        var recipe = Recipe.Create(familyId, name, userId, description);

        if (args.TryGetProperty("ingredients", out var ingredients))
        {
            foreach (var ing in ingredients.EnumerateArray())
            {
                var ingName = ing.GetProperty("name").GetString()!;
                var ingUnit = ing.GetProperty("unit").GetString()!;
                var quantity = ing.GetProperty("quantity").GetDecimal();

                // Find or create ingredient
                var ingredient = await _db.Ingredients
                    .FirstOrDefaultAsync(i => i.FamilyId == familyId && i.Name == ingName, ct);

                if (ingredient is null)
                {
                    ingredient = Ingredient.Create(familyId, ingName, ingUnit);
                    _db.Ingredients.Add(ingredient);
                    await _db.SaveChangesAsync(ct);
                }

                recipe.AddIngredient(ingredient.Id, quantity);
            }
        }

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new
        {
            success = true,
            recipeId = recipe.Id,
            name = recipe.Name,
            ingredientCount = recipe.Ingredients.Count
        });
    }
}
```

- [ ] **Step 8: Create write tools — AddToMealPlanTool**

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/AddToMealPlanTool.cs
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

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

    public async Task<string> ExecuteAsync(JsonElement args, Guid familyId, Guid userId, CancellationToken ct)
    {
        var recipeId = Guid.Parse(args.GetProperty("recipeId").GetString()!);
        var date = DateOnly.Parse(args.GetProperty("date").GetString()!);
        var slotStr = args.GetProperty("slot").GetString()!;

        if (!Enum.TryParse<Domain.Enums.MealSlot>(slotStr, true, out var slot))
            throw new DomainException($"Invalid meal slot: {slotStr}");

        var recipe = await _db.Recipes
            .FirstOrDefaultAsync(r => r.Id == recipeId && r.FamilyId == familyId, ct)
            ?? throw new DomainException("Recipe not found.");

        var entry = MealPlanEntry.Create(familyId, recipeId, date, slot, userId);
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
```

- [ ] **Step 9: Create write tools — CreateShoppingListTool + AddShoppingItemsTool**

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/CreateShoppingListTool.cs
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;

namespace MenuNest.Infrastructure.AI.Tools;

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

    public async Task<string> ExecuteAsync(JsonElement args, Guid familyId, Guid userId, CancellationToken ct)
    {
        var name = args.GetProperty("name").GetString()!;
        var list = ShoppingList.Create(familyId, name, userId);

        _db.ShoppingLists.Add(list);
        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new { success = true, shoppingListId = list.Id, name = list.Name });
    }
}
```

```csharp
// backend/src/MenuNest.Infrastructure/AI/Tools/AddShoppingItemsTool.cs
using System.Text.Json;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Infrastructure.AI.Tools;

public sealed class AddShoppingItemsTool : IToolDefinition
{
    private readonly IApplicationDbContext _db;

    public AddShoppingItemsTool(IApplicationDbContext db) => _db = db;

    public string Name => "add_shopping_items";
    public string Description => "เพิ่มรายการของที่ต้องซื้อเข้า shopping list";
    public bool RequiresConfirmation => true;

    public BinaryData ParametersSchema => BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "shoppingListId": { "type": "string", "format": "uuid", "description": "ID ของ shopping list" },
            "items": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string", "description": "ชื่อของ" },
                        "quantity": { "type": "number", "description": "จำนวน" },
                        "unit": { "type": "string", "description": "หน่วย" }
                    },
                    "required": ["name"]
                }
            }
        },
        "required": ["shoppingListId", "items"]
    }
    """);

    public async Task<string> ExecuteAsync(JsonElement args, Guid familyId, Guid userId, CancellationToken ct)
    {
        var listId = Guid.Parse(args.GetProperty("shoppingListId").GetString()!);

        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == listId && l.FamilyId == familyId, ct)
            ?? throw new DomainException("Shopping list not found.");

        var items = args.GetProperty("items");
        var addedCount = 0;

        foreach (var item in items.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var quantity = item.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 1m;
            var unit = item.TryGetProperty("unit", out var u) ? u.GetString() : null;

            list.AddItem(name, quantity, unit);
            addedCount++;
        }

        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new { success = true, addedCount, totalItems = list.Items.Count });
    }
}
```

- [ ] **Step 10: Verify build**

```bash
cd backend && dotnet build
```

Expected: Build succeeded. If there are compilation errors from entity method signatures not matching exactly, adjust the tool code to match the actual domain methods (check the entity source files for exact signatures).

- [ ] **Step 11: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/AI/Tools/
git commit -m "feat(ai): add tool definitions (5 read + 4 write)"
```

---

## Task 6: AI Chat Service

**Files:**
- Create: `backend/src/MenuNest.Infrastructure/AI/AzureOpenAiOptions.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/ChatSystemPrompt.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/IAiChatService.cs`
- Create: `backend/src/MenuNest.Infrastructure/AI/AzureOpenAiChatService.cs`
- Modify: `backend/src/MenuNest.Infrastructure/MenuNest.Infrastructure.csproj`

- [ ] **Step 1: Add Azure.AI.OpenAI NuGet package**

```bash
cd backend/src/MenuNest.Infrastructure && dotnet add package Azure.AI.OpenAI
```

- [ ] **Step 2: Create AzureOpenAiOptions**

```csharp
// backend/src/MenuNest.Infrastructure/AI/AzureOpenAiOptions.cs
namespace MenuNest.Infrastructure.AI;

public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAi";

    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class AzureSpeechOptions
{
    public const string SectionName = "AzureSpeech";

    public string Region { get; set; } = string.Empty;
    public string SubscriptionKey { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Create ChatSystemPrompt**

```csharp
// backend/src/MenuNest.Infrastructure/AI/ChatSystemPrompt.cs
namespace MenuNest.Infrastructure.AI;

public static class ChatSystemPrompt
{
    public static string Build(string familyName, int memberCount) => $"""
        คุณเป็นผู้ช่วยที่ปรึกษาเรื่องอาหารของครอบครัว "{familyName}" ({memberCount} คน)
        
        ## กฎ
        - ตอบเป็นภาษาไทยเสมอ
        - ใช้ tools ที่มีในการดึงข้อมูลจริง ห้ามแต่งสูตรหรือวัตถุดิบขึ้นเอง
        - เมื่อผู้ใช้ขอให้ทำอะไร (เพิ่ม meal plan, สร้าง shopping list, สร้างสูตร) ให้สรุปสิ่งที่จะทำก่อน แล้วถามยืนยัน ห้ามเรียก write tools โดยไม่ถามก่อน
        - เมื่อค้นหาสูตรแล้วไม่พบ ให้เสนอ 2 ทาง: 1) สร้างสูตรใหม่ 2) หาเมนูใกล้เคียง
        - พูดสั้น กระชับ เป็นกันเอง
        
        ## การตอบแบบ structured
        เมื่อแนะนำสูตรอาหาร ให้แนบ JSON block ในข้อความเพื่อแสดง recipe card:
        ```json
        {{"type":"recipe_cards","cards":[{{"recipeId":"guid","name":"ชื่อเมนู","stockMatch":"3/5"}}]}}
        ```
        
        เมื่อเสนอ actions ที่ต้องยืนยัน ให้แนบ:
        ```json
        {{"type":"confirmation","actions":[{{"tool":"tool_name","description":"คำอธิบาย"}}]}}
        ```
        
        ## วันนี้
        วันนี้คือ {DateTime.UtcNow:yyyy-MM-dd} ({DateTime.UtcNow:dddd})
        """;
}
```

- [ ] **Step 4: Create IAiChatService interface**

```csharp
// backend/src/MenuNest.Infrastructure/AI/IAiChatService.cs
using MenuNest.Domain.Entities;

namespace MenuNest.Infrastructure.AI;

public sealed record AiChatResponse(
    string Content,
    string? ToolCallsJson,
    string? StructuredDataJson,
    bool HasPendingWriteActions);

public interface IAiChatService
{
    Task<AiChatResponse> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        string userMessage,
        Guid familyId,
        Guid userId,
        CancellationToken ct);

    Task<AiChatResponse> ExecutePendingActionsAsync(
        IReadOnlyList<ChatMessage> history,
        string pendingToolCallsJson,
        Guid familyId,
        Guid userId,
        CancellationToken ct);
}
```

- [ ] **Step 5: Create AzureOpenAiChatService**

```csharp
// backend/src/MenuNest.Infrastructure/AI/AzureOpenAiChatService.cs
using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Enums;
using MenuNest.Infrastructure.AI.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

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
        IReadOnlyList<ChatMessage> history,
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
        IReadOnlyList<ChatMessage> history,
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

        var options = new ChatCompletionOptions(); // No tools needed for summary
        var response = await _chatClient.CompleteChatAsync(messages, options, ct);

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
        IReadOnlyList<ChatMessage> history,
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
                case ChatRole.User:
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
```

- [ ] **Step 6: Verify build**

```bash
cd backend && dotnet build
```

Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add backend/src/MenuNest.Infrastructure/AI/ backend/src/MenuNest.Infrastructure/MenuNest.Infrastructure.csproj
git commit -m "feat(ai): add AzureOpenAiChatService with tool calling loop"
```

---

## Task 7: SendMessage Handler

**Files:**
- Create: `backend/src/MenuNest.Application/UseCases/Chat/SendMessage/SendMessageCommand.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/SendMessage/SendMessageHandler.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/GetSpeechToken/GetSpeechTokenQuery.cs`
- Create: `backend/src/MenuNest.Application/UseCases/Chat/GetSpeechToken/GetSpeechTokenHandler.cs`

- [ ] **Step 1: Create SendMessageCommand + Handler**

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/SendMessage/SendMessageCommand.cs
using Mediator;

namespace MenuNest.Application.UseCases.Chat.SendMessage;

public sealed record SendMessageCommand(
    Guid ConversationId,
    string Content) : ICommand<SendMessageResponseDto>;
```

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/SendMessage/SendMessageHandler.cs
using System.Text.Json;
using Mediator;
using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using MenuNest.Domain.Exceptions;
using MenuNest.Infrastructure.AI;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Chat.SendMessage;

public sealed class SendMessageHandler : ICommandHandler<SendMessageCommand, SendMessageResponseDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;
    private readonly IAiChatService _aiChatService;

    public SendMessageHandler(
        IApplicationDbContext db,
        IUserProvisioner userProvisioner,
        IAiChatService aiChatService)
    {
        _db = db;
        _userProvisioner = userProvisioner;
        _aiChatService = aiChatService;
    }

    public async ValueTask<SendMessageResponseDto> Handle(
        SendMessageCommand command,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Content))
            throw new DomainException("Message cannot be empty.");

        var (user, familyId) = await _userProvisioner.RequireFamilyAsync(ct);

        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == command.ConversationId && c.UserId == user.Id, ct)
            ?? throw new DomainException("Conversation not found.");

        // Load history
        var history = await _db.ChatMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        // Save user message
        var userMsg = ChatMessage.CreateUserMessage(conversation.Id, command.Content);
        _db.ChatMessages.Add(userMsg);

        // Check if this is a confirmation of pending actions
        var lastAssistant = history.LastOrDefault(m =>
            m.Role == Domain.Enums.ChatRole.Assistant && m.ToolCalls is not null);

        AiChatResponse aiResponse;

        if (lastAssistant?.ToolCalls is not null && IsConfirmation(command.Content))
        {
            aiResponse = await _aiChatService.ExecutePendingActionsAsync(
                history, lastAssistant.ToolCalls, familyId, user.Id, ct);
        }
        else
        {
            aiResponse = await _aiChatService.ChatAsync(
                history, command.Content, familyId, user.Id, ct);
        }

        // Save assistant response
        var assistantMsg = ChatMessage.CreateAssistantMessage(
            conversation.Id,
            aiResponse.Content,
            aiResponse.ToolCallsJson,
            aiResponse.StructuredDataJson);

        _db.ChatMessages.Add(assistantMsg);

        // Update conversation title from first user message
        if (history.Count == 0)
        {
            var title = command.Content.Length > 100 ? command.Content[..100] : command.Content;
            conversation.UpdateTitle(title);
        }
        else
        {
            conversation.Touch();
        }

        await _db.SaveChangesAsync(ct);

        return new SendMessageResponseDto(
            assistantMsg.Id,
            "Assistant",
            assistantMsg.Content,
            assistantMsg.StructuredData,
            assistantMsg.CreatedAt);
    }

    private static bool IsConfirmation(string message)
    {
        var lower = message.Trim().ToLowerInvariant();
        var confirmPatterns = new[]
        {
            "ได้เลย", "ยืนยัน", "ตกลง", "โอเค", "ok", "yes", "ได้", "เอา", "ทำเลย",
            "ใช่", "confirm", "ดำเนินการ"
        };
        return confirmPatterns.Any(p => lower.Contains(p));
    }
}
```

- [ ] **Step 2: Create GetSpeechTokenQuery + Handler**

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/GetSpeechToken/GetSpeechTokenQuery.cs
using Mediator;

namespace MenuNest.Application.UseCases.Chat.GetSpeechToken;

public sealed record GetSpeechTokenQuery : IQuery<SpeechTokenDto>;
```

```csharp
// backend/src/MenuNest.Application/UseCases/Chat/GetSpeechToken/GetSpeechTokenHandler.cs
using System.Net.Http.Json;
using Mediator;
using MenuNest.Infrastructure.AI;
using Microsoft.Extensions.Options;

namespace MenuNest.Application.UseCases.Chat.GetSpeechToken;

public sealed class GetSpeechTokenHandler : IQueryHandler<GetSpeechTokenQuery, SpeechTokenDto>
{
    private readonly AzureSpeechOptions _options;
    private readonly HttpClient _httpClient;

    public GetSpeechTokenHandler(IOptions<AzureSpeechOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async ValueTask<SpeechTokenDto> Handle(GetSpeechTokenQuery query, CancellationToken ct)
    {
        var url = $"https://{_options.Region}.api.cognitive.microsoft.com/sts/v1.0/issueToken";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.SubscriptionKey);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadAsStringAsync(ct);

        return new SpeechTokenDto(token, _options.Region);
    }
}
```

- [ ] **Step 3: Verify build**

```bash
cd backend && dotnet build
```

- [ ] **Step 4: Commit**

```bash
git add backend/src/MenuNest.Application/UseCases/Chat/SendMessage/ backend/src/MenuNest.Application/UseCases/Chat/GetSpeechToken/
git commit -m "feat(ai): add SendMessage and GetSpeechToken handlers"
```

---

## Task 8: ChatController + Backend DI

**Files:**
- Create: `backend/src/MenuNest.WebApi/Controllers/ChatController.cs`
- Modify: `backend/src/MenuNest.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create ChatController**

```csharp
// backend/src/MenuNest.WebApi/Controllers/ChatController.cs
using Mediator;
using MenuNest.Application.UseCases.Chat;
using MenuNest.Application.UseCases.Chat.CreateConversation;
using MenuNest.Application.UseCases.Chat.DeleteConversation;
using MenuNest.Application.UseCases.Chat.GetMessages;
using MenuNest.Application.UseCases.Chat.GetSpeechToken;
using MenuNest.Application.UseCases.Chat.ListConversations;
using MenuNest.Application.UseCases.Chat.SendMessage;
using Microsoft.AspNetCore.Mvc;

namespace MenuNest.WebApi.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChatController(IMediator mediator) => _mediator = mediator;

    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyList<ConversationSummaryDto>>> ListConversations(
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ListConversationsQuery(), ct);
        return Ok(result);
    }

    [HttpPost("conversations")]
    public async Task<ActionResult<ConversationSummaryDto>> CreateConversation(
        CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateConversationCommand(), ct);
        return CreatedAtAction(nameof(ListConversations), result);
    }

    [HttpGet("conversations/{id:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetMessages(
        Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMessagesQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("conversations/{id:guid}/messages")]
    public async Task<ActionResult<SendMessageResponseDto>> SendMessage(
        Guid id,
        [FromBody] SendMessageRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new SendMessageCommand(id, request.Content), ct);
        return Ok(result);
    }

    [HttpDelete("conversations/{id:guid}")]
    public async Task<IActionResult> DeleteConversation(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteConversationCommand(id), ct);
        return NoContent();
    }

    [HttpGet("speech-token")]
    public async Task<ActionResult<SpeechTokenDto>> GetSpeechToken(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSpeechTokenQuery(), ct);
        return Ok(result);
    }
}

public sealed record SendMessageRequest(string Content);
```

- [ ] **Step 2: Update Infrastructure DependencyInjection**

Add to the existing `AddInfrastructure` method in `backend/src/MenuNest.Infrastructure/DependencyInjection.cs`:

```csharp
// AI services
services.Configure<AzureOpenAiOptions>(configuration.GetSection(AzureOpenAiOptions.SectionName));
services.Configure<AzureSpeechOptions>(configuration.GetSection(AzureSpeechOptions.SectionName));

services.AddHttpClient();

// Register all tools
services.AddScoped<IToolDefinition, SearchRecipesTool>();
services.AddScoped<IToolDefinition, CheckStockTool>();
services.AddScoped<IToolDefinition, GetMealPlanTool>();
services.AddScoped<IToolDefinition, GetShoppingListsTool>();
services.AddScoped<IToolDefinition, GetFamilyInfoTool>();
services.AddScoped<IToolDefinition, CreateRecipeTool>();
services.AddScoped<IToolDefinition, AddToMealPlanTool>();
services.AddScoped<IToolDefinition, CreateShoppingListTool>();
services.AddScoped<IToolDefinition, AddShoppingItemsTool>();

services.AddScoped<IAiChatService, AzureOpenAiChatService>();
```

Add the required `using` statements at the top:

```csharp
using MenuNest.Infrastructure.AI;
using MenuNest.Infrastructure.AI.Tools;
```

- [ ] **Step 3: Add configuration to appsettings.json**

Add to `backend/src/MenuNest.WebApi/appsettings.json`:

```json
"AzureOpenAi": {
    "Endpoint": "",
    "DeploymentName": "gpt-4o",
    "ApiKey": ""
},
"AzureSpeech": {
    "Region": "southeastasia",
    "SubscriptionKey": ""
}
```

Actual values should go in `.env` or user secrets, not committed.

- [ ] **Step 4: Verify build**

```bash
cd backend && dotnet build
```

Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add backend/src/MenuNest.WebApi/Controllers/ChatController.cs backend/src/MenuNest.Infrastructure/DependencyInjection.cs backend/src/MenuNest.WebApi/appsettings.json
git commit -m "feat(ai): add ChatController and register AI services"
```

---

## Task 9: Frontend — Packages + Syncfusion Setup

**Files:**
- Modify: `frontend/package.json`
- Modify: `frontend/src/main.tsx`

- [ ] **Step 1: Install packages**

```bash
cd frontend && npm install @syncfusion/react-interactive-chat@^33.1.44 microsoft-cognitiveservices-speech-sdk
```

- [ ] **Step 2: Add Syncfusion CSS import to main.tsx**

Add after the existing Syncfusion CSS imports in `frontend/src/main.tsx`:

```typescript
import '@syncfusion/react-interactive-chat/styles/material.css'
```

- [ ] **Step 3: Commit**

```bash
git add frontend/package.json frontend/package-lock.json frontend/src/main.tsx
git commit -m "feat(ai): install interactive-chat and speech SDK packages"
```

---

## Task 10: Frontend — RTK Query Chat Endpoints

**Files:**
- Modify: `frontend/src/shared/api/api.ts`

- [ ] **Step 1: Add types and tag type**

Add `'ChatConversations'` and `'ChatMessages'` to the `tagTypes` array in `api.ts`.

- [ ] **Step 2: Add chat endpoint types above the `createApi` call**

```typescript
// Chat types
export interface ConversationSummaryDto {
  id: string
  title: string
  createdAt: string
  updatedAt: string | null
}

export interface ChatMessageDto {
  id: string
  role: string
  content: string
  structuredData: string | null
  createdAt: string
}

export interface SendMessageResponseDto {
  messageId: string
  role: string
  content: string
  structuredData: string | null
  createdAt: string
}

export interface SpeechTokenDto {
  token: string
  region: string
}
```

- [ ] **Step 3: Add chat endpoints inside the `endpoints` builder**

```typescript
// Chat - Conversations
listConversations: build.query<ConversationSummaryDto[], void>({
    query: () => '/api/chat/conversations',
    providesTags: ['ChatConversations'],
}),
createConversation: build.mutation<ConversationSummaryDto, void>({
    query: () => ({
        url: '/api/chat/conversations',
        method: 'POST',
    }),
    invalidatesTags: ['ChatConversations'],
}),
deleteConversation: build.mutation<void, string>({
    query: (id) => ({
        url: `/api/chat/conversations/${id}`,
        method: 'DELETE',
    }),
    invalidatesTags: ['ChatConversations'],
}),

// Chat - Messages
getChatMessages: build.query<ChatMessageDto[], string>({
    query: (conversationId) => `/api/chat/conversations/${conversationId}/messages`,
    providesTags: (_result, _err, id) => [{ type: 'ChatMessages', id }],
}),
sendChatMessage: build.mutation<SendMessageResponseDto, { conversationId: string; content: string }>({
    query: ({ conversationId, content }) => ({
        url: `/api/chat/conversations/${conversationId}/messages`,
        method: 'POST',
        body: { content },
    }),
    invalidatesTags: (_result, _err, { conversationId }) => [
        { type: 'ChatMessages', id: conversationId },
        'ChatConversations',
    ],
}),

// Chat - Speech
getSpeechToken: build.query<SpeechTokenDto, void>({
    query: () => '/api/chat/speech-token',
}),
```

- [ ] **Step 4: Export hooks at the bottom of api.ts**

Add to the existing exports:

```typescript
useListConversationsQuery,
useCreateConversationMutation,
useDeleteConversationMutation,
useGetChatMessagesQuery,
useSendChatMessageMutation,
useGetSpeechTokenQuery,
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/shared/api/api.ts
git commit -m "feat(ai): add RTK Query chat endpoints"
```

---

## Task 11: Frontend — Redux Slice + Hooks

**Files:**
- Create: `frontend/src/pages/ai-assistant/aiAssistantSlice.ts`
- Create: `frontend/src/pages/ai-assistant/hooks/useAzureSpeech.ts`
- Create: `frontend/src/pages/ai-assistant/hooks/useAiAssistant.ts`
- Modify: `frontend/src/store/index.ts`

- [ ] **Step 1: Create aiAssistantSlice**

```typescript
// frontend/src/pages/ai-assistant/aiAssistantSlice.ts
import { createSlice } from '@reduxjs/toolkit'
import type { PayloadAction } from '@reduxjs/toolkit'

interface AiAssistantState {
  activeConversationId: string | null
  isRecording: boolean
}

const initialState: AiAssistantState = {
  activeConversationId: null,
  isRecording: false,
}

const aiAssistantSlice = createSlice({
  name: 'aiAssistant',
  initialState,
  reducers: {
    setActiveConversation(state, action: PayloadAction<string | null>) {
      state.activeConversationId = action.payload
    },
    setIsRecording(state, action: PayloadAction<boolean>) {
      state.isRecording = action.payload
    },
  },
})

export const { setActiveConversation, setIsRecording } = aiAssistantSlice.actions
export default aiAssistantSlice.reducer
```

- [ ] **Step 2: Register slice in store**

Add to `frontend/src/store/index.ts`:

Import: `import aiAssistantSlice from '../pages/ai-assistant/aiAssistantSlice'`

Add to reducer object: `aiAssistant: aiAssistantSlice,`

- [ ] **Step 3: Create useAzureSpeech hook**

```typescript
// frontend/src/pages/ai-assistant/hooks/useAzureSpeech.ts
import { useCallback, useRef, useState } from 'react'
import * as SpeechSDK from 'microsoft-cognitiveservices-speech-sdk'
import { useGetSpeechTokenQuery } from '../../../shared/api/api'

export function useAzureSpeech() {
  const { data: tokenData } = useGetSpeechTokenQuery()
  const [isListening, setIsListening] = useState(false)
  const [transcript, setTranscript] = useState('')
  const [error, setError] = useState<string | null>(null)
  const recognizerRef = useRef<SpeechSDK.SpeechRecognizer | null>(null)

  const startListening = useCallback(() => {
    if (!tokenData) {
      setError('Speech token not available')
      return
    }

    setError(null)
    setTranscript('')

    const speechConfig = SpeechSDK.SpeechConfig.fromAuthorizationToken(
      tokenData.token,
      tokenData.region,
    )
    speechConfig.speechRecognitionLanguage = 'th-TH'

    const audioConfig = SpeechSDK.AudioConfig.fromDefaultMicrophoneInput()
    const recognizer = new SpeechSDK.SpeechRecognizer(speechConfig, audioConfig)

    recognizer.recognizing = (_sender, event) => {
      setTranscript(event.result.text)
    }

    recognizer.recognized = (_sender, event) => {
      if (event.result.reason === SpeechSDK.ResultReason.RecognizedSpeech) {
        setTranscript(event.result.text)
      }
    }

    recognizer.canceled = (_sender, event) => {
      if (event.reason === SpeechSDK.CancellationReason.Error) {
        setError('ไม่สามารถรับเสียงได้ ลองใหม่อีกครั้ง')
      }
      setIsListening(false)
    }

    recognizerRef.current = recognizer
    recognizer.startContinuousRecognitionAsync(
      () => setIsListening(true),
      (err) => {
        setError(String(err))
        setIsListening(false)
      },
    )
  }, [tokenData])

  const stopListening = useCallback(() => {
    recognizerRef.current?.stopContinuousRecognitionAsync(
      () => {
        setIsListening(false)
        recognizerRef.current?.close()
        recognizerRef.current = null
      },
      () => setIsListening(false),
    )
  }, [])

  return { isListening, transcript, error, startListening, stopListening, setTranscript }
}
```

- [ ] **Step 4: Create useAiAssistant hook**

```typescript
// frontend/src/pages/ai-assistant/hooks/useAiAssistant.ts
import { useCallback } from 'react'
import {
  useListConversationsQuery,
  useCreateConversationMutation,
  useDeleteConversationMutation,
  useGetChatMessagesQuery,
  useSendChatMessageMutation,
} from '../../../shared/api/api'
import { useAppDispatch, useAppSelector } from '../../../store'
import { setActiveConversation } from '../aiAssistantSlice'

export function useAiAssistant() {
  const dispatch = useAppDispatch()
  const activeConversationId = useAppSelector((s) => s.aiAssistant.activeConversationId)

  const { data: conversations, isLoading: isLoadingConversations } = useListConversationsQuery()
  const { data: messages, isLoading: isLoadingMessages } = useGetChatMessagesQuery(
    activeConversationId!,
    { skip: !activeConversationId },
  )

  const [createConversation] = useCreateConversationMutation()
  const [deleteConversation] = useDeleteConversationMutation()
  const [sendMessage, { isLoading: isSending }] = useSendChatMessageMutation()

  const handleNewConversation = useCallback(async () => {
    const result = await createConversation().unwrap()
    dispatch(setActiveConversation(result.id))
  }, [createConversation, dispatch])

  const handleSelectConversation = useCallback(
    (id: string) => {
      dispatch(setActiveConversation(id))
    },
    [dispatch],
  )

  const handleDeleteConversation = useCallback(
    async (id: string) => {
      await deleteConversation(id).unwrap()
      if (activeConversationId === id) {
        dispatch(setActiveConversation(null))
      }
    },
    [deleteConversation, activeConversationId, dispatch],
  )

  const handleSendMessage = useCallback(
    async (content: string) => {
      if (!activeConversationId || !content.trim()) return

      await sendMessage({
        conversationId: activeConversationId,
        content: content.trim(),
      }).unwrap()
    },
    [activeConversationId, sendMessage],
  )

  return {
    conversations: conversations ?? [],
    messages: messages ?? [],
    activeConversationId,
    isLoadingConversations,
    isLoadingMessages,
    isSending,
    handleNewConversation,
    handleSelectConversation,
    handleDeleteConversation,
    handleSendMessage,
  }
}
```

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/ai-assistant/aiAssistantSlice.ts frontend/src/pages/ai-assistant/hooks/ frontend/src/store/index.ts
git commit -m "feat(ai): add Redux slice, useAzureSpeech, and useAiAssistant hooks"
```

---

## Task 12: Frontend — Chat Components

**Files:**
- Create: `frontend/src/pages/ai-assistant/components/RecipeCard.tsx`
- Create: `frontend/src/pages/ai-assistant/components/ConfirmationMessage.tsx`

- [ ] **Step 1: Create RecipeCard component**

```tsx
// frontend/src/pages/ai-assistant/components/RecipeCard.tsx
import { useNavigate } from 'react-router-dom'
import { Button, Color, Variant } from '@syncfusion/react-buttons'

interface RecipeCardProps {
  recipeId: string
  name: string
  stockMatch?: string
  onAddToMealPlan?: () => void
  onCreateShoppingList?: () => void
}

export function RecipeCard({
  recipeId,
  name,
  stockMatch,
  onAddToMealPlan,
  onCreateShoppingList,
}: RecipeCardProps) {
  const navigate = useNavigate()

  return (
    <div className="ai-recipe-card">
      <div className="ai-recipe-card__header">
        <span className="ai-recipe-card__name">{name}</span>
        {stockMatch && <span className="ai-recipe-card__stock">วัตถุดิบ {stockMatch}</span>}
      </div>
      <div className="ai-recipe-card__actions">
        {onAddToMealPlan && (
          <Button
            variant={Variant.Filled}
            color={Color.Primary}
            onClick={onAddToMealPlan}
          >
            + Meal Plan
          </Button>
        )}
        <Button
          variant={Variant.Outlined}
          color={Color.Primary}
          onClick={() => navigate(`/recipes/${recipeId}`)}
        >
          ดูสูตร
        </Button>
        {onCreateShoppingList && (
          <Button
            variant={Variant.Outlined}
            color={Color.Primary}
            onClick={onCreateShoppingList}
          >
            🛒 ซื้อของ
          </Button>
        )}
      </div>
    </div>
  )
}
```

- [ ] **Step 2: Create ConfirmationMessage component**

```tsx
// frontend/src/pages/ai-assistant/components/ConfirmationMessage.tsx
import { Button, Color, Variant } from '@syncfusion/react-buttons'

interface ConfirmationMessageProps {
  onConfirm: () => void
  onReject: () => void
  disabled?: boolean
}

export function ConfirmationMessage({ onConfirm, onReject, disabled }: ConfirmationMessageProps) {
  return (
    <div className="ai-confirmation">
      <div className="ai-confirmation__buttons">
        <Button
          variant={Variant.Filled}
          color={Color.Primary}
          onClick={onConfirm}
          disabled={disabled}
        >
          ✅ ยืนยัน
        </Button>
        <Button
          variant={Variant.Outlined}
          color={Color.Primary}
          onClick={onReject}
          disabled={disabled}
        >
          ❌ ยกเลิก
        </Button>
      </div>
    </div>
  )
}
```

- [ ] **Step 3: Commit**

```bash
git add frontend/src/pages/ai-assistant/components/
git commit -m "feat(ai): add RecipeCard and ConfirmationMessage components"
```

---

## Task 13: Frontend — AI Assistant Page

**Files:**
- Create: `frontend/src/pages/ai-assistant/AiAssistantPage.tsx`
- Create: `frontend/src/pages/ai-assistant/index.ts`

- [ ] **Step 1: Create AiAssistantPage**

```tsx
// frontend/src/pages/ai-assistant/AiAssistantPage.tsx
import { useCallback, useMemo, useRef, useState } from 'react'
import { Button, Color, Variant } from '@syncfusion/react-buttons'
import { useAiAssistant } from './hooks/useAiAssistant'
import { useAzureSpeech } from './hooks/useAzureSpeech'
import { useCurrentUser } from '../../shared/hooks/useCurrentUser'
import { RecipeCard } from './components/RecipeCard'
import { ConfirmationMessage } from './components/ConfirmationMessage'
import type { ChatMessageDto } from '../../shared/api/api'

export function AiAssistantPage() {
  const { displayName } = useCurrentUser()
  const {
    conversations,
    messages,
    activeConversationId,
    isLoadingMessages,
    isSending,
    handleNewConversation,
    handleSelectConversation,
    handleDeleteConversation,
    handleSendMessage,
  } = useAiAssistant()

  const { isListening, transcript, error: speechError, startListening, stopListening, setTranscript } =
    useAzureSpeech()

  const [inputValue, setInputValue] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  // Map messages to Syncfusion Chat format
  const chatMessages = useMemo(
    () =>
      messages.map((m) => ({
        id: m.id,
        text: m.content,
        author: {
          id: m.role === 'User' ? 'user' : 'assistant',
          user: m.role === 'User' ? displayName : 'AI Assistant',
        },
        timestamp: new Date(m.createdAt),
      })),
    [messages, displayName],
  )

  const currentUser = useMemo(
    () => ({ id: 'user', user: displayName }),
    [displayName],
  )

  const onSend = useCallback(async () => {
    const text = inputValue.trim() || transcript.trim()
    if (!text) return

    setInputValue('')
    setTranscript('')
    await handleSendMessage(text)
  }, [inputValue, transcript, handleSendMessage, setTranscript])

  const onMicPress = useCallback(() => {
    if (isListening) {
      stopListening()
    } else {
      startListening()
    }
  }, [isListening, startListening, stopListening])

  const handleConfirm = useCallback(() => {
    handleSendMessage('ได้เลย ยืนยัน')
  }, [handleSendMessage])

  const handleReject = useCallback(() => {
    handleSendMessage('ยกเลิก ไม่ต้องทำ')
  }, [handleSendMessage])

  // Parse structured data from the last message
  const lastMessage = messages[messages.length - 1]
  const structuredData = useMemo(() => {
    if (!lastMessage?.structuredData) return null
    try {
      return JSON.parse(lastMessage.structuredData)
    } catch {
      return null
    }
  }, [lastMessage])

  return (
    <section className="page page--ai-assistant">
      <header className="page__header">
        <h1>AI Assistant</h1>
        <Button variant={Variant.Filled} color={Color.Primary} onClick={handleNewConversation}>
          + บทสนทนาใหม่
        </Button>
      </header>

      <div className="ai-assistant-layout">
        {/* Conversation list */}
        <aside className="ai-assistant-sidebar">
          {conversations.map((c) => (
            <div
              key={c.id}
              className={`ai-conversation-item ${c.id === activeConversationId ? 'ai-conversation-item--active' : ''}`}
              onClick={() => handleSelectConversation(c.id)}
            >
              <span className="ai-conversation-item__title">{c.title}</span>
              <button
                className="ai-conversation-item__delete"
                onClick={(e) => {
                  e.stopPropagation()
                  handleDeleteConversation(c.id)
                }}
              >
                ×
              </button>
            </div>
          ))}
        </aside>

        {/* Chat area */}
        <div className="ai-assistant-chat">
          {!activeConversationId ? (
            <div className="ai-assistant-empty">
              <p>เลือกบทสนทนาหรือสร้างใหม่เพื่อเริ่มคุยกับ AI</p>
            </div>
          ) : (
            <>
              {isLoadingMessages ? (
                <div className="ai-assistant-loading">กำลังโหลด...</div>
              ) : (
                <div className="ai-chat-messages">
                  {messages.map((m) => (
                    <div
                      key={m.id}
                      className={`ai-chat-bubble ai-chat-bubble--${m.role.toLowerCase()}`}
                    >
                      <div className="ai-chat-bubble__content">{m.content}</div>
                      {m.structuredData && renderStructuredData(m)}
                    </div>
                  ))}
                  {isSending && (
                    <div className="ai-chat-bubble ai-chat-bubble--assistant ai-chat-bubble--loading">
                      <span>กำลังคิด...</span>
                    </div>
                  )}
                </div>
              )}

              {/* Structured data actions for last message */}
              {structuredData?.type === 'confirmation' && (
                <ConfirmationMessage
                  onConfirm={handleConfirm}
                  onReject={handleReject}
                  disabled={isSending}
                />
              )}

              {/* Input bar */}
              <div className="ai-input-bar">
                <input
                  ref={inputRef}
                  className="ai-input-bar__text"
                  type="text"
                  placeholder="ถามเรื่องอาหาร..."
                  value={isListening ? transcript : inputValue}
                  onChange={(e) => setInputValue(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && !e.shiftKey && onSend()}
                  disabled={isSending}
                />
                <button
                  className={`ai-input-bar__mic ${isListening ? 'ai-input-bar__mic--active' : ''}`}
                  onMouseDown={onMicPress}
                  onMouseUp={() => isListening && stopListening()}
                  onTouchStart={onMicPress}
                  onTouchEnd={() => isListening && stopListening()}
                  disabled={isSending}
                >
                  🎤
                </button>
                <button
                  className="ai-input-bar__send"
                  onClick={onSend}
                  disabled={isSending || (!inputValue.trim() && !transcript.trim())}
                >
                  ➤
                </button>
              </div>
              {speechError && <p className="ai-speech-error">{speechError}</p>}
            </>
          )}
        </div>
      </div>
    </section>
  )

  function renderStructuredData(msg: ChatMessageDto) {
    if (!msg.structuredData) return null
    try {
      const data = JSON.parse(msg.structuredData)
      if (data.type === 'recipe_cards' && data.cards) {
        return (
          <div className="ai-recipe-cards">
            {data.cards.map((card: { recipeId: string; name: string; stockMatch?: string }) => (
              <RecipeCard key={card.recipeId} {...card} />
            ))}
          </div>
        )
      }
      return null
    } catch {
      return null
    }
  }
}
```

- [ ] **Step 2: Create index.ts barrel export**

```typescript
// frontend/src/pages/ai-assistant/index.ts
export { AiAssistantPage } from './AiAssistantPage'
```

- [ ] **Step 3: Add page styles**

Add to `frontend/src/index.css` (or create a separate CSS file and import it):

```css
/* AI Assistant */
.ai-assistant-layout {
  display: flex;
  gap: 16px;
  height: calc(100vh - 140px);
}

.ai-assistant-sidebar {
  width: 240px;
  flex-shrink: 0;
  overflow-y: auto;
  border-right: 1px solid var(--color-border, #e0e0e0);
  padding-right: 12px;
}

.ai-conversation-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 12px;
  border-radius: 8px;
  cursor: pointer;
  margin-bottom: 4px;
}

.ai-conversation-item:hover,
.ai-conversation-item--active {
  background: var(--color-surface-variant, #e8def8);
}

.ai-conversation-item__title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
  font-size: 14px;
}

.ai-conversation-item__delete {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 18px;
  color: #999;
  padding: 0 4px;
}

.ai-assistant-chat {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.ai-assistant-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  flex: 1;
  color: #999;
}

.ai-chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.ai-chat-bubble {
  max-width: 80%;
  padding: 10px 14px;
  border-radius: 16px;
  font-size: 14px;
  line-height: 1.5;
  white-space: pre-wrap;
}

.ai-chat-bubble--user {
  align-self: flex-end;
  background: #6750a4;
  color: white;
  border-radius: 16px 4px 16px 16px;
}

.ai-chat-bubble--assistant {
  align-self: flex-start;
  background: #e8def8;
  color: #1c1b1f;
  border-radius: 4px 16px 16px 16px;
}

.ai-chat-bubble--loading {
  opacity: 0.7;
}

.ai-input-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border-top: 1px solid #e0e0e0;
}

.ai-input-bar__text {
  flex: 1;
  border: 1px solid #ddd;
  border-radius: 24px;
  padding: 10px 16px;
  font-size: 14px;
  outline: none;
}

.ai-input-bar__text:focus {
  border-color: #6750a4;
}

.ai-input-bar__mic,
.ai-input-bar__send {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  flex-shrink: 0;
}

.ai-input-bar__mic {
  background: #e8def8;
}

.ai-input-bar__mic--active {
  background: #d32f2f;
  animation: pulse 1s infinite;
}

.ai-input-bar__send {
  background: #6750a4;
  color: white;
}

.ai-input-bar__send:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.ai-speech-error {
  color: #d32f2f;
  font-size: 12px;
  padding: 0 12px;
  margin: 0;
}

.ai-recipe-card {
  border: 1px solid #ddd;
  border-radius: 12px;
  overflow: hidden;
  background: white;
  margin-top: 8px;
}

.ai-recipe-card__header {
  padding: 10px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.ai-recipe-card__name {
  font-weight: 600;
  font-size: 14px;
}

.ai-recipe-card__stock {
  font-size: 12px;
  color: #4caf50;
}

.ai-recipe-card__actions {
  display: flex;
  gap: 6px;
  padding: 8px 10px;
  border-top: 1px solid #eee;
}

.ai-confirmation__buttons {
  display: flex;
  gap: 8px;
  padding: 8px 12px;
}

@keyframes pulse {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.1); }
}

/* Mobile: hide sidebar */
@media (max-width: 768px) {
  .ai-assistant-sidebar {
    display: none;
  }
}
```

- [ ] **Step 4: Commit**

```bash
git add frontend/src/pages/ai-assistant/ frontend/src/index.css
git commit -m "feat(ai): add AiAssistantPage with chat UI, voice input, and recipe cards"
```

---

## Task 14: Frontend — Router + NavBar Integration

**Files:**
- Modify: `frontend/src/router.tsx`
- Modify: `frontend/src/shared/components/NavBar.tsx`

- [ ] **Step 1: Add route to router.tsx**

Add import at top of `frontend/src/router.tsx`:

```typescript
import { AiAssistantPage } from './pages/ai-assistant'
```

Add route inside the `AppLayout` children array (after the `/family` route, before the catch-all):

```typescript
{ path: '/ai-assistant', element: <AiAssistantPage /> },
```

- [ ] **Step 2: Add NavBar item**

In `frontend/src/shared/components/NavBar.tsx`, add to the `navItems` array:

```typescript
{ to: '/ai-assistant', label: 'AI Assistant' },
```

- [ ] **Step 3: Verify build**

```bash
cd frontend && npm run build
```

Expected: Build succeeded with no errors.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/router.tsx frontend/src/shared/components/NavBar.tsx
git commit -m "feat(ai): add AI Assistant to router and navigation"
```

---

## Task 15: Manual Testing Checklist

- [ ] **Step 1: Start backend**

```bash
cd backend/src/MenuNest.WebApi && dotnet run
```

Verify: Swagger UI loads at `/scalar/v2`, ChatController endpoints visible.

- [ ] **Step 2: Start frontend**

```bash
cd frontend && npm run dev
```

Verify: App loads, "AI Assistant" appears in NavBar.

- [ ] **Step 3: Test basic flow**

1. Navigate to `/ai-assistant`
2. Click "+ บทสนทนาใหม่" → conversation appears in sidebar
3. Type "มีอะไรในสต็อกบ้าง" → AI responds with stock data
4. Type "แนะนำเมนูเย็นหน่อย" → AI responds with recipe cards
5. Test voice: press and hold mic → speak → release → text appears in input
6. Test confirmation: "เพิ่มผัดกะเพราเข้า meal plan เย็นนี้" → AI proposes → click confirm → AI executes

- [ ] **Step 4: Fix any issues found during testing**

Adjust code as needed based on actual Syncfusion component API, entity method signatures, or Azure service responses.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "feat(ai): AI Assistant feature complete — text + voice chat with tool use"
```
