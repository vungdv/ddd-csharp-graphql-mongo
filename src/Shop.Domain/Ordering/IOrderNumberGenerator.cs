using System.Security.Cryptography;

namespace Shop.Domain.Ordering;

/// <summary>Produces human-readable order numbers; injectable so tests are deterministic.</summary>
public interface IOrderNumberGenerator
{
    /// <summary>Returns <c>ORD-{yyyyMMdd}-{6 alphanumerics}</c> for the UTC date of <paramref name="now"/>.</summary>
    string Next(DateTimeOffset now);
}

public sealed class RandomOrderNumberGenerator : IOrderNumberGenerator
{
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    public const int SuffixLength = 6;

    public static RandomOrderNumberGenerator Instance { get; } = new();

    public string Next(DateTimeOffset now)
    {
        var suffix = RandomNumberGenerator.GetString(Alphabet, SuffixLength);
        return $"ORD-{now.UtcDateTime:yyyyMMdd}-{suffix}";
    }
}
