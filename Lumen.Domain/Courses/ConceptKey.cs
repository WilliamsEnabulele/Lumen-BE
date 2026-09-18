using System.Security.Cryptography;
using System.Text;

namespace Lumen.Domain.Courses;

/// <summary>
/// A concept's identity, derived from what the concept *is* rather than from where it landed
/// in a particular ingestion run.
///
/// This exists for one requirement: reprocessing an edited source document must update the
/// lesson graph without discarding student progress. That only works if a concept keeps the
/// same identity across runs — so the key cannot be a fresh Guid per run, and it cannot be
/// the concept's position, because inserting a paragraph would renumber everything after it.
///
/// Identity comes from the course and the concept's <b>title</b>, not its body. That is the
/// whole point: an instructor rewriting the explanation of "nested loops" has not created a
/// different concept, and every mastery record pointing at it must survive the edit. Whether
/// the body changed is a separate question with a separate answer — see
/// <see cref="ContentFingerprint"/>.
///
/// The cost of this choice, stated plainly: renaming a concept orphans its history. That is
/// the right way round — a rename is rare and reviewable, a rewrite is constant.
/// </summary>
public static class ConceptKey
{
    /// <summary>Length of the returned key. 24 hex characters is 96 bits — far past collision risk here.</summary>
    public const int Length = 24;

    public static string From(Guid courseId, string title)
    {
        var normalised = Normalise(title);
        if (normalised.Length == 0)
            throw new ArgumentException("A concept must have a title to have an identity.", nameof(title));

        var payload = Encoding.UTF8.GetBytes($"{courseId:N}\u001e{normalised}");
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()[..Length];
    }

    /// <summary>
    /// Case, punctuation and whitespace are not identity. "Nested Loops", "nested loops" and
    /// "Nested  loops!" are the same concept, so a tidy-up pass over a source document does
    /// not silently re-key the whole course.
    /// </summary>
    internal static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }
}
