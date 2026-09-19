using Lumen.Domain.Scripts;

namespace Lumen.Domain.Retrieval;

/// <summary>
/// Answering a student's question from the lesson in front of them.
///
/// Two behaviours matter more than the ranking. The first is that the answer names the node it
/// came from, so a wrong answer can be traced to the passage that produced it. The second is
/// that a question this lesson does not cover is <b>declared out of scope</b> rather than
/// answered anyway: a tutor that guesses confidently is worse than one that says it does not
/// know, because a student cannot tell the difference and will not check.
///
/// Scoring is term overlap with a proximity bonus, which is deliberately modest. Its job is to
/// make the honest behaviour the default before any model is involved; a better retriever
/// replaces the ranking without touching either rule above.
/// </summary>
public static class LessonRetrieval
{
    /// <summary>Below this, the lesson does not cover the question and we say so.</summary>
    public const double InScopeThreshold = 0.18;

    /// <summary>How much being near what we are currently teaching is worth.</summary>
    public const double ProximityWeight = 0.15;

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "do", "does", "did", "can",
        "could", "would", "should", "what", "why", "how", "when", "where", "who", "which", "that",
        "this", "these", "those", "it", "its", "of", "to", "in", "on", "for", "with", "and", "or",
        "but", "if", "so", "as", "at", "by", "from", "about", "i", "you", "me", "my", "your",
        "again", "please", "tell", "explain", "mean", "means", "meaning"
    };

    public static RetrievalResult Answer(
        IReadOnlyList<ScriptNode> nodes,
        string question,
        Guid? currentNodeId = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var terms = Terms(question);
        if (terms.Count == 0 || nodes.Count == 0) return RetrievalResult.OutOfScope;

        var currentOrdinal = currentNodeId is null
            ? (int?)null
            : nodes.FirstOrDefault(node => node.Id == currentNodeId)?.Ordinal;

        ScriptNode? best = null;
        var bestScore = 0.0;

        foreach (var node in nodes)
        {
            if (node.Kind == ScriptNodeKind.CheckForUnderstanding) continue;

            var score = Overlap(terms, node.Text);
            if (currentOrdinal is not null)
            {
                var distance = Math.Abs(node.Ordinal - currentOrdinal.Value);
                score += ProximityWeight / (1 + distance);
            }

            if (score <= bestScore) continue;
            bestScore = score;
            best = node;
        }

        if (best is null || bestScore < InScopeThreshold) return RetrievalResult.OutOfScope;

        return new RetrievalResult(best.Text, best.Id, best.SourceRef, bestScore, InScope: true);
    }

    private static double Overlap(IReadOnlyCollection<string> questionTerms, string text)
    {
        var nodeTerms = Terms(text);
        if (nodeTerms.Count == 0) return 0;

        var hits = questionTerms.Count(term => nodeTerms.Contains(term));
        return (double)hits / questionTerms.Count;
    }

    private static HashSet<string> Terms(string text)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = new string(raw.Where(char.IsLetterOrDigit).ToArray());
            if (word.Length < 3 || Stopwords.Contains(word)) continue;
            terms.Add(Stem(word));
        }

        return terms;
    }

    /// <summary>
    /// Crude suffix stripping, so "loops" matches "loop" and "nesting" matches "nested". Not
    /// linguistics — just enough that a student's plural does not miss the passage that answers
    /// them.
    /// </summary>
    private static string Stem(string word)
    {
        var lower = word.ToLowerInvariant();

        foreach (var suffix in new[] { "ing", "ed", "es", "s" })
        {
            if (lower.Length > suffix.Length + 2 && lower.EndsWith(suffix, StringComparison.Ordinal))
                return lower[..^suffix.Length];
        }

        return lower;
    }
}

public sealed record RetrievalResult(
    string Text,
    Guid? SourceNodeId,
    string? SourceRef,
    double Score,
    bool InScope)
{
    public static readonly RetrievalResult OutOfScope = new(
        "That is outside this lesson — it is not in the material I have been given. I can come back to it, but I would be guessing if I answered now.",
        null, null, 0, false);
}
