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
