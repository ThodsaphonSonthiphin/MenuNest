namespace MenuNest.Domain.Enums;

/// <summary>A per-hour cue a Weather window is judged on, chosen per call from the User's question
/// (menunest-220). Heat (Feels-like) and Sun (UV index) stay separate (menunest-221). The declared
/// order is the tie-break order when naming the blocking signal of an empty result (menunest-225).</summary>
public enum WeatherSignal
{
    Rain,
    Heat,
    Sun,
}
