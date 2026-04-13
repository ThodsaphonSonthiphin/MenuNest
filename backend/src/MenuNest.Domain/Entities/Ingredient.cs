using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// An ingredient in the family's master list. Each ingredient is
/// pinned to a single unit so that recipes and stock can be compared
/// arithmetically without conversion.
/// </summary>
public sealed class Ingredient : Entity
{
    public Guid FamilyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Unit { get; private set; } = null!;

    // EF Core
    private Ingredient() { }

    public static Ingredient Create(Guid familyId, string name, string unit)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Ingredient name is required.");
        }
        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new DomainException("Ingredient unit is required.");
        }

        return new Ingredient
        {
            FamilyId = familyId,
            Name = name.Trim(),
            Unit = unit.Trim()
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Ingredient name is required.");
        }
        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void ChangeUnit(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new DomainException("Ingredient unit is required.");
        }
        Unit = unit.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
