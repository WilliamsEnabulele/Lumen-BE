using System.Security.Cryptography;
using System.Text;

namespace Lumen.Infrastructure.Billing;

/// <summary>
/// Whether a webhook really came from Monnify.
///
/// Monnify computes an HMAC-SHA512 of the request body keyed with the merchant's client secret
/// and sends it in the <c>monnify-signature</c> header. Without this check, the endpoint grants
/// subscriptions to anyone who learns its URL.
///
/// Two details decide whether an implementation of this is correct or merely looks correct.
/// The hash is over the <em>raw</em> body, byte for byte as it arrived — deserialising and
/// re-serialising changes whitespace and key order and produces a different hash, which is the
/// single most common way this ends up permanently failing. And the comparison is constant
/// time, because a comparison that returns early leaks, one byte at a time, how close a guess
/// was.
/// </summary>
public static class MonnifySignature
{
    public const string Header = "monnify-signature";

    public static bool IsValid(string rawBody, string? signature, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrEmpty(clientSecret)) return false;

        var expected = Compute(rawBody, clientSecret);

        // Hex, so the length is fixed and known. A length mismatch is decided before any
        // comparison, which is safe: the length of a hash is not a secret.
        if (signature.Length != expected.Length) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(signature.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(expected));
    }

    /// <summary>The hash as Monnify computes it: lowercase hex of the HMAC-SHA512.</summary>
    public static string Compute(string rawBody, string clientSecret)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        ArgumentNullException.ThrowIfNull(clientSecret);

        using var mac = new HMACSHA512(Encoding.UTF8.GetBytes(clientSecret));
        return Convert.ToHexString(mac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
    }
}
