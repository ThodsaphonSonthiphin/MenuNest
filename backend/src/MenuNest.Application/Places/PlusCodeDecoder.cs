using Olc = Google.OpenLocationCode.OpenLocationCode;

namespace MenuNest.Application.Places;

public enum PlusCodeKind { Invalid = 0, Full, Short }

/// <summary>
/// Offline Plus Code decode (spec R5.2, ticket #57). Costs nothing and makes no
/// network call: the Trips searchText resolver returns zero results for every Plus
/// Code (R5.1), and Geocoding — which does work — is $5/1k and, on the wrong
/// locality, confidently ~500 km off. A SHORT code therefore requires an explicit
/// reference point from the caller; it is never guessed from the map camera.
///
/// Every entry point returns null rather than throwing: the package throws
/// ArgumentException for anything unparseable, and a bad paste is normal user
/// input, not an exceptional condition.
/// </summary>
public static class PlusCodeDecoder
{
    public static PlusCodeKind Classify(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return PlusCodeKind.Invalid;
        var c = code.Trim();
        if (Olc.IsFull(c)) return PlusCodeKind.Full;
        if (Olc.IsShort(c)) return PlusCodeKind.Short;
        return PlusCodeKind.Invalid;
    }

    public static (double Lat, double Lng)? DecodeFull(string? code)
    {
        if (Classify(code) != PlusCodeKind.Full) return null;
        try
        {
            var area = Olc.Decode(code!.Trim());
            return (area.CenterLatitude, area.CenterLongitude);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static (double Lat, double Lng)? DecodeShort(string? code, double refLat, double refLng)
    {
        if (Classify(code) != PlusCodeKind.Short) return null;
        try
        {
            var full = Olc.ShortCode.RecoverNearest(code!.Trim(), refLat, refLng);
            var area = Olc.Decode(full.Code);
            return (area.CenterLatitude, area.CenterLongitude);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
