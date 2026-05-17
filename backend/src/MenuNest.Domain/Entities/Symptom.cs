using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A symptom name (e.g., "ปวดหัว", "ไข้"). Hybrid catalog:
/// seed rows (<see cref="IsSeed"/> == true, <see cref="UserId"/> == null)
/// are shared across all users; user-custom rows are scoped to one user.
/// </summary>
public sealed class Symptom : Entity
{
    public string Name { get; private set; } = null!;
    public bool IsSeed { get; private set; }
    public Guid? UserId { get; private set; }

    // EF Core
    private Symptom() { }

    public static Symptom CreateSeed(string name, Guid? fixedId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Symptom name is required.");

        return new Symptom
        {
            Id = fixedId ?? Guid.NewGuid(),
            Name = name.Trim(),
            IsSeed = true,
            UserId = null
        };
    }

    public static Symptom CreateCustom(string name, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Symptom name is required.");
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required for custom symptom.");

        return new Symptom
        {
            Name = name.Trim(),
            IsSeed = false,
            UserId = userId
        };
    }

    public void Rename(string newName)
    {
        if (IsSeed)
            throw new DomainException("Seed symptoms cannot be renamed.");
        if (string.IsNullOrWhiteSpace(newName))
            throw new DomainException("Symptom name is required.");

        Name = newName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
