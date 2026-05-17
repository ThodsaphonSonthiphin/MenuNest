namespace MenuNest.Domain.Enums;

/// <summary>
/// How much the symptom episode interfered with daily activities.
/// Aggregated into MIDAS-style disability days in the doctor report.
/// </summary>
public enum FunctionalImpact
{
    None = 1,
    Mild = 2,
    Moderate = 3,
    SevereBedrest = 4
}
