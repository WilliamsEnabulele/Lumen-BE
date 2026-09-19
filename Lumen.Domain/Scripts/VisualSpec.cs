namespace Lumen.Domain.Scripts;

/// <summary>
/// What the canvas shows, and how it moves.
///
/// A visual is not a picture dropped next to the words — it is animated against the speech,
/// which is why the kind is a behaviour rather than a file. The client advances it from the
/// utterance offset it is already tracking, so a student who interrupts and resumes
/// mid-sentence gets the illustration back at the same frame, not restarted.
/// </summary>
public enum VisualKind
{
    /// <summary>Nothing on the canvas. The words carry it alone.</summary>
    None = 0,

    /// <summary>Code, with the active line lit as the explanation reaches it.</summary>
    Code = 1,

    /// <summary>Points that arrive one at a time, in step with being said.</summary>
    Steps = 2,

    /// <summary>Two things held side by side, the second arriving after the first.</summary>
    Compare = 3,

    /// <summary>A quantity that counts, for anything that repeats or accumulates.</summary>
    Counter = 4,

    /// <summary>A line worth leaving on screen — a definition, a rule.</summary>
    Statement = 5
}

/// <param name="Payload">
/// Newline-separated, and read according to <see cref="Kind"/>: the source for Code, one line
/// per point for Steps, two blocks for Compare, "from|to|label" for Counter.
/// </param>
public sealed record VisualSpec(VisualKind Kind, string Payload)
{
    public static readonly VisualSpec None = new(VisualKind.None, string.Empty);

    public bool IsEmpty => Kind == VisualKind.None || Payload.Length == 0;
}
