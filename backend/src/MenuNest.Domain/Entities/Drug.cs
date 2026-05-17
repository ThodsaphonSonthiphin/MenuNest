using MenuNest.Domain.Common;
using MenuNest.Domain.Enums;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.Entities;

/// <summary>
/// A medication entry in the user's personal drug master. Owns its dose
/// rules (<c>EffectDurationMinHours</c>..<c>MaxHours</c>, <c>MaxDailyDose</c>),
/// inventory (<c>StockCount</c>, <c>ExpirationDate</c>), and the set of
/// symptoms it can treat. Photos are stored separately on <see cref="Photo"/>
/// via <see cref="PhotoParentType.Drug"/>.
/// </summary>
public sealed class Drug : Entity
{
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? ActiveIngredient { get; private set; }
    public DrugType DrugType { get; private set; }
    public string DoseStrength { get; private set; } = null!;
    public int EffectDurationMinHours { get; private set; }
    public int EffectDurationMaxHours { get; private set; }
    public int MaxDailyDose { get; private set; }
    public int StockCount { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public string? UsageNote { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    private List<Guid> _treatsSymptomIds = new();
    public IReadOnlyList<Guid> TreatsSymptomIds => _treatsSymptomIds.AsReadOnly();

    // EF Core
    private Drug() { }

    public static Drug Create(
        Guid userId,
        string name,
        DrugType drugType,
        string doseStrength,
        int effectDurationMinHours,
        int effectDurationMaxHours,
        int maxDailyDose,
        int stockCount = 0,
        string? activeIngredient = null,
        DateOnly? expirationDate = null,
        string? usageNote = null,
        IEnumerable<Guid>? treatsSymptomIds = null)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Drug name is required.");
        if (string.IsNullOrWhiteSpace(doseStrength))
            throw new DomainException("Dose strength is required (e.g., '500mg').");
        if (effectDurationMinHours <= 0 || effectDurationMaxHours < effectDurationMinHours)
            throw new DomainException("Effect duration range is invalid.");
        if (maxDailyDose <= 0)
            throw new DomainException("Max daily dose must be positive.");
        if (stockCount < 0)
            throw new DomainException("Stock count cannot be negative.");

        return new Drug
        {
            UserId = userId,
            Name = name.Trim(),
            ActiveIngredient = activeIngredient?.Trim(),
            DrugType = drugType,
            DoseStrength = doseStrength.Trim(),
            EffectDurationMinHours = effectDurationMinHours,
            EffectDurationMaxHours = effectDurationMaxHours,
            MaxDailyDose = maxDailyDose,
            StockCount = stockCount,
            ExpirationDate = expirationDate,
            UsageNote = usageNote?.Trim(),
            _treatsSymptomIds = treatsSymptomIds?.Distinct().ToList() ?? new List<Guid>()
        };
    }

    public void UpdateProfile(
        string name,
        DrugType drugType,
        string doseStrength,
        int effectDurationMinHours,
        int effectDurationMaxHours,
        int maxDailyDose,
        string? activeIngredient,
        DateOnly? expirationDate,
        string? usageNote)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Drug name is required.");
        if (string.IsNullOrWhiteSpace(doseStrength))
            throw new DomainException("Dose strength is required.");
        if (effectDurationMinHours <= 0 || effectDurationMaxHours < effectDurationMinHours)
            throw new DomainException("Effect duration range is invalid.");
        if (maxDailyDose <= 0)
            throw new DomainException("Max daily dose must be positive.");

        Name = name.Trim();
        ActiveIngredient = activeIngredient?.Trim();
        DrugType = drugType;
        DoseStrength = doseStrength.Trim();
        EffectDurationMinHours = effectDurationMinHours;
        EffectDurationMaxHours = effectDurationMaxHours;
        MaxDailyDose = maxDailyDose;
        ExpirationDate = expirationDate;
        UsageNote = usageNote?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateStock(int newStock)
    {
        if (newStock < 0)
            throw new DomainException("Stock count cannot be negative.");
        StockCount = newStock;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DecrementStock(int by = 1)
    {
        if (by <= 0)
            throw new DomainException("Stock decrement must be positive.");
        StockCount = Math.Max(0, StockCount - by);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetTreats(IEnumerable<Guid> symptomIds)
    {
        _treatsSymptomIds = symptomIds?.Distinct().ToList() ?? new List<Guid>();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
