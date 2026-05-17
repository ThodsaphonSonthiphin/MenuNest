namespace MenuNest.Domain.Enums;

/// <summary>
/// Type of migraine aura experienced before or during an attack. Aura
/// presence affects medication selection (e.g., estrogen contraindicated
/// in migraine-with-aura due to stroke risk).
/// </summary>
public enum AuraType
{
    Visual = 1,
    Sensory = 2,
    Speech = 3,
    Motor = 4
}
