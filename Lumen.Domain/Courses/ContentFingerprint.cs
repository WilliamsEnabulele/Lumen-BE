using System.Security.Cryptography;
using System.Text;

namespace Lumen.Domain.Courses;

/// <summary>
/// Whether a concept's content changed, which is a different question from whether it is the
/// same concept — <see cref="ConceptKey"/> answers that one.
///
/// Reprocessing compares fingerprints to decide what actually needs regenerating: a concept
/// whose fingerprint is unchanged keeps its script, its pre-synthesised audio and its
/// assessment items, and costs nothing to reprocess. Only the ones that moved get rebuilt.
/// </summary>
public static class ContentFingerprint
{
    public const int Length = 16;

    public static string Of(string? body)
    {
        var normalised = ConceptKey.Normalise(body);
        var payload = Encoding.UTF8.GetBytes(normalised);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()[..Length];
    }
}
