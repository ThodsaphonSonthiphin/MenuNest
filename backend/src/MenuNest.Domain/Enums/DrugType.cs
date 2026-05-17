namespace MenuNest.Domain.Enums;

/// <summary>
/// Classification of a drug, used both for display and to drive clinical
/// guidance (e.g., Triptan dosing rules differ from NSAIDs).
/// </summary>
public enum DrugType
{
    Analgesic = 1,
    Nsaid = 2,
    Triptan = 3,
    Other = 9
}
