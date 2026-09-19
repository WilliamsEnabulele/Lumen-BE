namespace Lumen.Domain.Ingestion;

/// <summary>
/// Splits prose into utterances the length of a script node.
///
/// The target is not arbitrary. A node is the resolution of the resume pointer, so at fifteen
/// to thirty seconds — roughly twenty-five to seventy-five spoken words — a wrong resume drops
/// the student at most half a minute off, which is recoverable. At three minutes a node every
/// wrong resume would be visibly wrong and no amount of retrieval quality would fix it.
///
/// A sentence is never split. Half a sentence is not something a tutor can say.
/// </summary>
public static class Utterances
{
    public const int TargetWords = 55;
    public const int MaxWords = 85;

    public static IReadOnlyList<string> Split(string text, int targetWords = TargetWords, int maxWords = MaxWords)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var utterances = new List<string>();
        var current = new List<string>();
        var currentWords = 0;

        foreach (var sentence in Sentences(text))
        {
            var words = WordCount(sentence);

            // A single sentence longer than the ceiling still travels whole: cutting it would
            // produce something no tutor could say, and an over-long node is merely coarse.
            if (currentWords > 0 && currentWords + words > maxWords)
            {
                utterances.Add(string.Join(' ', current));
                current.Clear();
                currentWords = 0;
            }

            current.Add(sentence);
            currentWords += words;

            if (currentWords >= targetWords)
            {
                utterances.Add(string.Join(' ', current));
                current.Clear();
                currentWords = 0;
            }
        }

        if (current.Count > 0) utterances.Add(string.Join(' ', current));
        return utterances;
    }

    /// <summary>
    /// Sentence boundaries, erring toward leaving text joined. A missed split produces a long
    /// node; a wrong split produces a tutor stopping mid-thought, which is much worse.
    /// </summary>
    public static IReadOnlyList<string> Sentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?')) continue;

            // Look past any closing quote or bracket to the character that follows.
            var after = i + 1;
            while (after < text.Length && (text[after] is '"' or '\'' or ')' or ']')) after++;

            if (after >= text.Length)
            {
                break;
            }

            if (!char.IsWhiteSpace(text[after])) continue;

            var next = after;
            while (next < text.Length && char.IsWhiteSpace(text[next])) next++;

            // "e.g." and "Fig. 4" are not sentence ends; a capital or a quote after the space is.
            if (next < text.Length && !(char.IsUpper(text[next]) || text[next] is '"' or '\'')) continue;

            var sentence = text[start..after].Trim();
            if (sentence.Length > 0) sentences.Add(sentence);
            start = next;
            i = next - 1;
        }

        var tail = text[start..].Trim();
        if (tail.Length > 0) sentences.Add(tail);

        return sentences;
    }

    public static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
