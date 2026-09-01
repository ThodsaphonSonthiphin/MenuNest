using MenuNest.Domain.Enums;

namespace MenuNest.Application.UseCases.Budget.History.ListChanges;

/// <summary>
/// One row of the Change history sheet (menunest-195). The last four fields carry
/// every reason a row can be unpressable, so no client ever re-derives one
/// (menunest-216):
/// <list type="bullet">
/// <item><c>IsDead</c> — menunest-197: the Envelope is gone, so NOBODY can act on
/// this row, the family head included. It is the only thing that greys the row.</item>
/// <item><c>CanUndo</c> — menunest-198/216: you may undo what you authored; the
/// family head may undo anyone's.</item>
/// <item><c>CanRedo</c> — menunest-216: you may redo what you UNDID, not what you
/// authored. So the head's undo sticks, and the author cannot reverse it.</item>
/// <item><c>BlockedReason</c> — why the applicable button is off. One field is
/// enough: a row shows either Undo or Redo, never both.</item>
/// </list>
/// <c>CanUndo</c> and <c>CanRedo</c> are two fields on purpose, not an oversight —
/// after the head undoes your change the row is still yours to see and not yours
/// to redo, which one flag cannot say.
/// </summary>
public sealed record BudgetChangeDto(
    Guid Id, Guid UserId, string UserDisplayName,
    BudgetChangeKind Kind, Guid? BatchId,
    string CategoryName, string? SecondCategoryName,
    decimal Delta, bool? FlagValue,
    bool IsUndone, string? UndoneByDisplayName,
    DateTime CreatedAt,
    bool CanUndo, bool CanRedo, bool IsDead, string? BlockedReason);
