using System.Security.Cryptography;

namespace Lumen.Domain.Billing;

/// <summary>
/// Our own reference for a payment.
///
/// Random rather than sequential, because it travels to the provider, comes back in a redirect
/// URL the student can read, and appears in support threads. A sequential one tells anybody
/// holding theirs roughly how many payments the business has taken, and lets them guess
/// somebody else's.
/// </summary>
public static class PaymentReference
{
    public const string Prefix = "lmn";

    public static string Next() =>
        $"{Prefix}_{DateTimeOffset.UtcNow:yyyyMMdd}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant()}";

    /// <summary>
    /// Whether a string is one of ours at all.
    ///
    /// Checked before anything is looked up, because the reference arrives from outside in both
    /// the webhook and the callback, and a lookup key that has not been shaped-checked is how
    /// a path or a query ends up somewhere it should not be.
    /// </summary>
    public static bool IsWellFormed(string? reference) =>
        reference is { Length: > 4 and <= 64 }
        && reference.StartsWith(Prefix + "_", StringComparison.Ordinal)
        && reference.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
