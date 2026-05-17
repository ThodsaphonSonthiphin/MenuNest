namespace MenuNest.Domain.Enums;

/// <summary>
/// User response to a follow-up ping. <c>Retro*</c> values are used by
/// the retro-close modal when the user reopens the app after missing
/// the live push.
/// </summary>
public enum PingResponse
{
    Resolved = 1,
    Improved = 2,
    Same = 3,
    Worse = 4,
    RetroResolved = 5,
    RetroUnknown = 6
}
