using FluentAssertions;
using MenuNest.Application.Places;
using Xunit;

namespace MenuNest.Application.UnitTests.Places;

public class PlusCodeDecoderTests
{
    [Theory]
    [InlineData("7P52QG42+GP", PlusCodeKind.Full)]
    [InlineData("7p52qg42+gp", PlusCodeKind.Full)]   // case-insensitive
    [InlineData("42+GP", PlusCodeKind.Short)]
    [InlineData("not a code", PlusCodeKind.Invalid)]
    [InlineData("", PlusCodeKind.Invalid)]
    [InlineData("13.7563, 100.5018", PlusCodeKind.Invalid)]
    public void ClassifiesTheThreeCases(string code, PlusCodeKind expected) =>
        PlusCodeDecoder.Classify(code).Should().Be(expected);

    [Fact]
    public void DecodesAFullCodeToItsCentre()
    {
        // 7P52QG42+GP is central Bangkok. A full code is a deterministic offline
        // decode — no reference point, no network, no cost.
        var p = PlusCodeDecoder.DecodeFull("7P52QG42+GP");

        p.Should().NotBeNull();
        p!.Value.Lat.Should().BeApproximately(13.7563, 0.01);
        p.Value.Lng.Should().BeApproximately(100.5018, 0.01);
    }

    [Fact]
    public void DecodesAShortCodeAgainstItsReferencePoint()
    {
        var p = PlusCodeDecoder.DecodeShort("42+GP", 13.75, 100.50);

        p.Should().NotBeNull();
        p!.Value.Lat.Should().BeApproximately(13.7563, 0.01);
        p.Value.Lng.Should().BeApproximately(100.5018, 0.01);
    }

    [Fact]
    public void TheSameShortCodeResolvesElsewhereFromAnotherReference()
    {
        // The reason R5.2 refuses to guess the locality: the identical short code
        // recovers to a completely different place from a different reference.
        var bangkok = PlusCodeDecoder.DecodeShort("42+GP", 13.75, 100.50)!.Value;
        var chiangmai = PlusCodeDecoder.DecodeShort("42+GP", 18.79, 98.98)!.Value;

        GeoDistanceForTest(bangkok, chiangmai).Should().BeGreaterThan(100_000);
    }

    [Fact]
    public void ReturnsNullRatherThanThrowingOnGarbage()
    {
        PlusCodeDecoder.DecodeFull("not a code").Should().BeNull();
        PlusCodeDecoder.DecodeShort("not a code", 13.75, 100.50).Should().BeNull();
        PlusCodeDecoder.DecodeFull("42+GP").Should().BeNull(); // short passed to full
    }

    private static double GeoDistanceForTest((double Lat, double Lng) a, (double Lat, double Lng) b)
    {
        var dLat = (a.Lat - b.Lat) * 111_000;
        var dLng = (a.Lng - b.Lng) * 111_000 * Math.Cos(a.Lat * Math.PI / 180);
        return Math.Sqrt(dLat * dLat + dLng * dLng);
    }
}
