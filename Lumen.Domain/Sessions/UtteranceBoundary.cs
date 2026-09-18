namespace Lumen.Domain.Sessions;

/// <summary>
/// Where to re-enter an utterance that was interrupted part-way through.
///
/// The corridor test made the case for this. Resuming at the exact character the tutor
/// stopped on is precise, and it sounds wrong: you land mid-clause, on a fragment, with no
/// re-entry, and it reads as a stutter rather than as a teacher picking the thread back up.
/// A person rewinds to the start of the thought and says it again.
///
/// So the two concerns are kept apart. <see cref="ResumePointer"/> stores the exact offset,
/// because that is the truth about what the student actually heard. This decides where to
/// start speaking again, which is a judgement, and a different one.
/// </summary>
public static class UtteranceBoundary
{
    /// <summary>
    /// How far back a resume may rewind. Long enough to reach the start of a normal sentence,
    /// short enough that a student who interrupted late in a long clause does not have to sit
    /// through a paragraph they already heard.
    /// </summary>
    public const int DefaultMaxRewind = 180;

    public static int SnapBack(string text, int offset, int maxRewind = DefaultMaxRewind)
    {
        if (string.IsNullOrEmpty(text) || offset <= 0) return 0;

        var cursor = Math.Min(offset, text.Length);
        var floor = Math.Max(0, cursor - maxRewind);

        // The start of the sentence we are inside, if it is within reach.
        var strong = FindBreak(text, cursor, floor, static ch => ch is '.' or '!' or '?');
        if (strong >= 0) return Math.Min(SkipLeadingSpace(text, strong + 1), cursor);

        // Failing that, the start of the clause.
        var weak = FindBreak(text, cursor, floor, static ch => ch is ',' or ';' or ':' or '—');
        if (weak >= 0) return Math.Min(SkipLeadingSpace(text, weak + 1), cursor);

        // Nothing to rewind to, so at the very least do not resume half-way through a word.
        return StartOfWord(text, cursor);
    }

    private static int FindBreak(string text, int cursor, int floor, Func<char, bool> isBreak)
    {
        for (var i = cursor - 1; i >= floor; i--)
            if (isBreak(text[i])) return i;

        return -1;
    }

    private static int SkipLeadingSpace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static int StartOfWord(string text, int cursor)
    {
        var i = cursor;
        while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;
        return i;
    }
}
