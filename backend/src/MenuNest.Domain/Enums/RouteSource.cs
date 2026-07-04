namespace MenuNest.Domain.Enums;

/// <summary>Whether a Leg's distance/time is a real routed value (Routes API) or a
/// straight-line estimate (Haversine fallback). Named by quality, not vendor (ADR-018).</summary>
public enum RouteSource
{
    Routed,
    Estimated,
}
