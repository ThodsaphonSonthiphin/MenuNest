namespace MenuNest.Domain.Enums;

/// <summary>
/// Why an episode was logged without a corresponding intake. Captured
/// so the doctor report can surface medication-gap events (e.g.,
/// patient ran out of acute med while symptom was active).
/// </summary>
public enum NoDrugReason
{
    MaxDoseReached = 1,
    AllDrugsActive = 2,
    OutOfStock = 3,
    NoDrugTreatsThis = 4,
    UserSkip = 9
}
