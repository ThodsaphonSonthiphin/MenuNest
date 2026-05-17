using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A trigger that may precede a symptom episode (e.g., "เครียด", "นอนน้อย").
/// Same hybrid seed/custom pattern as <see cref="Symptom"/>.
/// </summary>
public sealed class Trigger : Entity
{
    public string Name { get; private set; } = null!;
    public bool IsSeed { get; private set; }
    public Guid? UserId { get; private set; }

    // EF Core
    private Trigger() { }

    public static Trigger CreateSeed(string name, Guid? fixedId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Trigger name is required.");

        return new Trigger
        {
            Id = fixedId ?? Guid.NewGuid(),
            Name = name.Trim(),
            IsSeed = true,
            UserId = null
        };
    }

    public static Trigger CreateCustom(string name, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Trigger name is required.");
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required for custom trigger.");

        return new Trigger
        {
            Name = name.Trim(),
            IsSeed = false,
            UserId = userId
        };
    }

    public void Rename(string newName)
    {
        if (IsSeed)
            throw new DomainException("Seed triggers cannot be renamed.");
        if (string.IsNullOrWhiteSpace(newName))
            throw new DomainException("Trigger name is required.");

        Name = newName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
