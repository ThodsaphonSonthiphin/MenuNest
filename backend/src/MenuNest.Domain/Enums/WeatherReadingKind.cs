namespace MenuNest.Domain.Enums;

/// <summary>Which weather reading a request wants for a Stop: Now = current conditions at the
/// coordinates (real present moment); OnArrival = forecast at the Stop's scheduled arrival time.</summary>
public enum WeatherReadingKind
{
    Now,
    OnArrival,
}
