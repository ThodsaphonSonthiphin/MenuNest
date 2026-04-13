using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;

namespace MenuNest.Domain.Entities;

/// <summary>
/// An append-only audit entry for every change to <see cref="StockItem.Quantity"/>.
/// Used to undo cook actions and trace "how did this ingredient end up
/// at zero".
/// </summary>
public sealed class StockTransaction : Entity
{
    public Guid FamilyId { get; private set; }
    public Guid IngredientId { get; private set; }
    public decimal Delta { get; private set; }
    public StockTransactionSource Source { get; private set; }
    public Guid? SourceRefId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string? Notes { get; private set; }

    // EF Core
    private StockTransaction() { }

    public static StockTransaction Create(
        Guid familyId,
        Guid ingredientId,
        decimal delta,
        StockTransactionSource source,
        Guid? sourceRefId,
        Guid userId,
        string? notes = null)
    {
        return new StockTransaction
        {
            FamilyId = familyId,
            IngredientId = ingredientId,
            Delta = delta,
            Source = source,
            SourceRefId = sourceRefId,
            CreatedByUserId = userId,
            Notes = notes
        };
    }
}
