# AI Assistant Feature — Design Spec

## Overview

Add an AI-powered assistant to MenuNest that acts as a food consultant and task executor. Users can ask questions, get recommendations, and command the AI to perform actions (add meal plans, create shopping lists, create recipes) via text or voice on mobile.

## Decisions

| Aspect | Decision |
|--------|----------|
| Scope | AI consultant + task executor (read + write) |
| Data access | All system data (recipes, stock, meal plan, shopping lists, family) |
| LLM | Azure OpenAI Service with tool use (function calling) |
| Speech | Azure AI Speech SDK (client-side, real-time streaming, th-TH) |
| UI | Syncfusion Chat UI component — full page in NavBar |
| Voice UX | Mic button in input bar (press-and-hold, like LINE/WhatsApp) |
| Language | Thai only |
| History | Persistent — stored in SQL (ChatConversation + ChatMessage) |
| Response format | Rich — text + recipe cards + action buttons |
| Confirmation | Always confirm before write operations |
| Recipe not found | Offer two choices: create new recipe, or find similar existing ones |

## Architecture

```
┌─────────────────────────────────────┐
│         Mobile Browser              │
│  ┌─────────────────────────────┐    │
│  │  Syncfusion Chat UI         │    │
│  │  + Azure Speech SDK         │    │
│  │  + Action Button Renderer   │    │
│  └──────────┬──────────────────┘    │
└─────────────┼───────────────────────┘
              │ REST API
              ▼
┌─────────────────────────────────────┐
│         ASP.NET Core Backend        │
│  ┌──────────────┐  ┌────────────┐   │
│  │ChatController │  │ AI Service │   │
│  └──────┬───────┘  └─────┬──────┘   │
│         │                │          │
│         ▼                ▼          │
│  ┌────────────┐  ┌──────────────┐   │
│  │ Chat Store │  │ Tool Executor│   │
│  │ (EF Core)  │  │ (CQRS reuse)│   │
│  └────────────┘  └──────┬───────┘   │
│                         │           │
│         ┌───────────────┤           │
│         ▼               ▼           │
│  ┌──────────┐   ┌──────────────┐    │
│  │ SQL Server│   │Azure OpenAI │    │
│  └──────────┘   └──────────────┘    │
└─────────────────────────────────────┘
```

### Flow

1. User types or speaks a message — frontend sends text to `POST /api/chat/conversations/{id}/messages`
2. Backend loads chat history from DB, constructs system prompt (Thai food consultant persona + tool definitions)
3. Azure OpenAI reads the message and decides which tools to call
4. Tool Executor runs tools by dispatching through Mediator — reusing existing CQRS handlers
5. Tool results are sent back to Azure OpenAI for the next step (loop until final response)
6. Backend parses the final response into structured format (text + cards + actions) and saves to DB
7. Frontend renders via Syncfusion Chat UI with custom message templates for recipe cards and action buttons

### Confirmation Flow

All write operations require user confirmation before execution:

```
User: "เพิ่มเมนูผัดกะเพราสำหรับเย็นนี้"
  → AI calls search_recipes("ผัดกะเพรา") → found
  → AI calls check_stock(recipe.ingredients) → missing 2 items
  → AI responds (NO write yet):
    "จะทำสิ่งนี้ให้นะคะ:
     ✅ เพิ่มผัดกะเพราหมูสับเข้า meal plan เย็นวันนี้
     🛒 สร้าง shopping list สำหรับน้ำมันหอย, พริก (ที่ขาด)
     ยืนยันไหมคะ?"
  → User: "ได้เลย"
  → AI calls add_to_meal_plan + create_shopping_list + add_shopping_items
  → AI responds: "เพิ่มเรียบร้อยแล้วค่ะ"
```

### Recipe Not Found Flow

```
User: "เพิ่มเมนูต้มข่าไก่เย็นนี้"
  → AI calls search_recipes("ต้มข่าไก่") → not found
  → AI responds:
    "ไม่พบสูตรต้มข่าไก่ในระบบค่ะ จะให้:
     1) สร้างสูตรต้มข่าไก่ให้ใหม่
     2) หาเมนูใกล้เคียงที่มีอยู่แล้ว
     เลือกอันไหนดีคะ?"
```

## Frontend

### Page Structure

New page at route `/ai-assistant`, added to NavBar.

```
pages/
  ai-assistant/
    AiAssistantPage.tsx          ← main page component
    useAiAssistant.ts            ← hook: chat logic, send message, load history
    useAzureSpeech.ts            ← hook: Azure Speech SDK, mic recording
    components/
      RecipeCard.tsx             ← recipe card rendered inside chat bubbles
      ActionButtons.tsx          ← meal plan / view recipe / shopping list buttons
      ConfirmationMessage.tsx    ← confirmation prompt with approve/reject buttons
```

### Syncfusion Chat UI

- Use `@syncfusion/react-interactive-chat` ChatUI component
- Custom message template for rendering rich responses (recipe cards, action buttons, confirmation prompts)
- Standard text input with mic button and send button
- Conversation list sidebar (or dropdown on mobile) for switching between conversations

### Azure Speech SDK Integration

- `useAzureSpeech` hook manages the speech recognizer lifecycle
- Fetch short-lived token from `GET /api/chat/speech-token` on page load
- Press-and-hold mic button → start recognition → real-time text appears in input field
- Release → finalize text → user can review before sending
- Token auto-refresh before expiry

### Voice UX

- Mic button sits next to the text input and send button in the input bar
- Press and hold to start recording (visual indicator: pulsing mic icon + waveform)
- Release to stop — transcribed text appears in the input field for review
- User can edit the text before pressing send
- If speech recognition fails, show a brief inline error message

## Backend

### AI Service Layer

New folder in `MenuNest.Infrastructure`:

```
Infrastructure/
  AI/
    IAiChatService.cs                ← interface
    AzureOpenAiChatService.cs        ← orchestrate chat + tool calling loop
    AzureOpenAiOptions.cs            ← config (endpoint, deployment name, API key)
    ChatSystemPrompt.cs              ← system prompt builder (persona + formatting rules)
    StructuredResponseParser.cs      ← parse AI text → structured response with cards
    Tools/
      IToolDefinition.cs             ← interface: Name, Description, Schema, RequiresConfirmation, ExecuteAsync()
      SearchRecipesTool.cs           ← read: search recipes by name/ingredients
      CheckStockTool.cs              ← read: check stock levels for ingredients
      GetMealPlanTool.cs             ← read: get meal plan entries for date range
      GetShoppingListsTool.cs        ← read: get shopping lists
      GetFamilyInfoTool.cs           ← read: get family members info
      CreateRecipeTool.cs            ← write: create recipe + add ingredients
      AddToMealPlanTool.cs           ← write: add recipe to meal plan for a date/slot
      CreateShoppingListTool.cs      ← write: create new shopping list
      AddShoppingItemsTool.cs        ← write: add items to shopping list
```

### Tool Calling Loop

1. Load conversation history from DB (last N messages to fit token budget)
2. Build system prompt: Thai food consultant persona + response format instructions (JSON for structured data)
3. Send to Azure OpenAI with all tool definitions
4. If response contains `tool_calls`:
   - For read tools (`RequiresConfirmation = false`): execute immediately via Mediator, send results back to AI, continue loop
   - For write tools (`RequiresConfirmation = true`): **pause the loop** — do NOT execute. Instead, return a confirmation response to the frontend containing the proposed actions (tool names + parameters as human-readable descriptions)
5. When user confirms: frontend sends a new message (e.g., "ได้เลย"). Backend re-enters the loop, replays the pending write tool calls from the previous assistant message's `ToolCalls` JSON, executes them, and sends results back to AI for a final summary
6. When user rejects: frontend sends rejection message. AI acknowledges and moves on — no tools executed
7. Loop until AI produces a final text response (no more tool calls)
8. Parse final response → extract structured data (recipe references, actions)
9. Save all messages (user, assistant, tool) to DB
10. Return structured response to frontend

**How pending actions are tracked:** The assistant's confirmation message stores the proposed `tool_calls` in the `ToolCalls` JSON column. On user confirmation, the backend reads back these pending calls from the most recent assistant message and executes them. No separate pending-state table needed.

### Speech Token Endpoint

- `GET /api/chat/speech-token` returns a short-lived Azure Speech token
- Backend holds the Speech subscription key — never exposed to frontend
- Token has ~10 minute TTL, frontend refreshes proactively

### System Prompt

The system prompt instructs the AI to:

- Act as a Thai food consultant for the user's family
- Always respond in Thai
- Use the provided tools to access real data — never make up recipes or ingredients
- When user requests an action: summarize what will be done and ask for confirmation before calling write tools
- When a recipe is not found: offer to create it or find similar alternatives
- Format responses with structured JSON blocks for recipe cards when referencing recipes
- Keep responses concise and friendly

## Data Model

### New Entities

```csharp
public class ChatConversation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string Title { get; set; }          // auto-generated from first message
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; }
    public Family Family { get; set; }
    public ICollection<ChatMessage> Messages { get; set; }
}

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public ChatRole Role { get; set; }         // User, Assistant, Tool
    public string Content { get; set; }
    public string? ToolCalls { get; set; }     // JSON: tool calls made (if Role=Assistant)
    public string? ToolName { get; set; }      // tool name (if Role=Tool)
    public string? StructuredData { get; set; } // JSON: recipe cards + actions for frontend
    public DateTime CreatedAt { get; set; }

    public ChatConversation Conversation { get; set; }
}

public enum ChatRole
{
    User,
    Assistant,
    Tool
}
```

### EF Core Configuration

- `ChatConversation` indexed on `(UserId, FamilyId, UpdatedAt DESC)` for listing
- `ChatMessage` indexed on `(ConversationId, CreatedAt)` for loading history
- Cascade delete: deleting a conversation deletes its messages

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/chat/conversations` | List user's conversations (paginated, newest first) |
| `POST` | `/api/chat/conversations` | Create new conversation |
| `GET` | `/api/chat/conversations/{id}/messages` | Get messages for a conversation (paginated) |
| `POST` | `/api/chat/conversations/{id}/messages` | Send message → AI processes → returns response |
| `DELETE` | `/api/chat/conversations/{id}` | Delete conversation and all messages |
| `GET` | `/api/chat/speech-token` | Get Azure Speech short-lived token |

### Send Message Request/Response

**Request:**
```json
{
  "content": "เพิ่มเมนูผัดกะเพราสำหรับเย็นนี้"
}
```

**Response:**
```json
{
  "id": "guid",
  "role": "assistant",
  "content": "จะทำสิ่งนี้ให้นะคะ:\n✅ เพิ่มผัดกะเพราหมูสับเข้า meal plan เย็นวันนี้\n🛒 สร้าง shopping list สำหรับน้ำมันหอย, พริก",
  "structuredData": {
    "type": "confirmation",
    "actions": [
      { "tool": "add_to_meal_plan", "description": "เพิ่มผัดกะเพราหมูสับ — เย็นวันนี้" },
      { "tool": "create_shopping_list", "description": "สร้างรายการซื้อ: น้ำมันหอย, พริก" }
    ]
  },
  "createdAt": "2026-04-14T18:00:00Z"
}
```

## Azure Resources Required

| Resource | Purpose | Pricing Tier |
|----------|---------|-------------|
| Azure OpenAI Service | LLM (GPT-4o recommended for tool use) | Standard S0 |
| Azure AI Speech | Speech-to-text (th-TH) | Free tier (5h/month) or S0 |

### Configuration (backend .env)

```
AZURE_OPENAI_ENDPOINT=https://<name>.openai.azure.com/
AZURE_OPENAI_DEPLOYMENT=gpt-4o
AZURE_OPENAI_API_KEY=<key>
AZURE_SPEECH_REGION=southeastasia
AZURE_SPEECH_KEY=<key>
```

## Error Handling

- **Azure OpenAI unavailable:** Show friendly message "AI ไม่พร้อมใช้งานชั่วคราว ลองใหม่อีกครั้งนะคะ"
- **Tool execution fails:** AI reports the specific failure, does not retry write operations automatically
- **Speech recognition fails:** Inline error in input bar, user can retry or type instead
- **Token limit exceeded:** Truncate oldest messages from history, keep system prompt + recent context
- **Confirmation timeout:** No timeout — confirmation stays until user responds or starts a new topic
