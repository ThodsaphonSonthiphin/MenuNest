namespace MenuNest.Domain.Enums;

/// <summary>
/// Symptoms commonly associated with migraine attacks. Photophobia and
/// phonophobia are ICHD-3 criteria; presence of either plus nausea
/// indicates "with associated features" classification.
/// </summary>
public enum AssociatedSymptom
{
    Nausea = 1,
    Vomiting = 2,
    Photophobia = 3,
    Phonophobia = 4,
    Osmophobia = 5
}
