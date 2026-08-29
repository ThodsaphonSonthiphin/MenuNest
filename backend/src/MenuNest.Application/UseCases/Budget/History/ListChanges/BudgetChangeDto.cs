using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.History.ListChanges;

/// <summary>
/// One row of the Change history sheet (menunest-195). <c>CanUndo</c> and
/// <c>BlockedReason</c> carry menunest-197's rule: a row whose Envelope is gone
/// STAYS on the list, unpressable, saying why — it is never dropped.
/// </summary>
public sealed record BudgetChangeDto(
    Guid Id, Guid UserId, string UserDisplayName,
    BudgetChangeKind Kind, Guid? BatchId,
    string CategoryName, string? SecondCategoryName,
    decimal Delta, bool? FlagValue,
    bool IsUndone, string? UndoneByDisplayName,
    DateTime CreatedAt,
    bool CanUndo, string? BlockedReason);
