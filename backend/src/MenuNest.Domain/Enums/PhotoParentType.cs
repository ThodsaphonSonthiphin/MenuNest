namespace MenuNest.Domain.Enums;

/// <summary>
/// Discriminator for which entity a <c>Photo</c> belongs to. The
/// <c>(ParentType, ParentId)</c> pair takes the place of separate
/// per-entity FK columns so adding new photo-bearing entities later
/// requires no schema change.
/// </summary>
public enum PhotoParentType
{
    Drug = 1,
    SymptomEpisode = 2,
    Intake = 3
}
