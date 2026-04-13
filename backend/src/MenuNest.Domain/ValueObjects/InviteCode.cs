using System.Security.Cryptography;
using MenuNest.Domain.Common;
using MenuNest.Domain.Exceptions;

namespace MenuNest.Domain.ValueObjects;

/// <summary>
/// A human-readable invite code for joining a family, formatted as
/// <c>XXXX-XXXX</c> using an alphabet that excludes visually ambiguous
/// characters (0/O, 1/I/L).
/// </summary>
public sealed class InviteCode : ValueObject
{
    // Crockford-like alphabet: no O, 0, I, 1, L.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int HalfLength = 4;
    private const int TotalLength = HalfLength * 2 + 1; // "XXXX-XXXX"

    public string Value { get; }

    private InviteCode(string value)
    {
        Value = value;
    }

    public static InviteCode Generate()
    {
        Span<byte> bytes = stackalloc byte[HalfLength * 2];
        RandomNumberGenerator.Fill(bytes);

        Span<char> chars = stackalloc char[TotalLength];
        for (var i = 0; i < HalfLength; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }
        chars[HalfLength] = '-';
        for (var i = 0; i < HalfLength; i++)
        {
            chars[HalfLength + 1 + i] = Alphabet[bytes[HalfLength + i] % Alphabet.Length];
        }
        return new InviteCode(new string(chars));
    }

    public static InviteCode From(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainException("Invite code cannot be empty.");
        }

        var normalized = raw.Trim().ToUpperInvariant();

        if (normalized.Length != TotalLength || normalized[HalfLength] != '-')
        {
            throw new DomainException($"Invite code must be formatted as XXXX-XXXX.");
        }

        foreach (var c in normalized)
        {
            if (c == '-') continue;
            if (!Alphabet.Contains(c))
            {
                throw new DomainException($"Invite code contains an unsupported character '{c}'.");
            }
        }

        return new InviteCode(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    public static implicit operator string(InviteCode code) => code.Value;
}
