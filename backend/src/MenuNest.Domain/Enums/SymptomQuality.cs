namespace MenuNest.Domain.Enums;

/// <summary>
/// Pain quality descriptor for a symptom episode. Throbbing is one of the
/// ICHD-3 diagnostic criteria for migraine without aura.
/// </summary>
public enum SymptomQuality
{
    Throbbing = 1,
    Pressure = 2,
    Stabbing = 3,
    Burning = 4
}
