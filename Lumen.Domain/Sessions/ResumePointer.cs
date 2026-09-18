using Lumen.Domain.Scripts;

namespace Lumen.Domain.Sessions;

/// <summary>
/// Where teaching picks back up. Persisted, first-class, and never inferred from conversation
/// history — inference is what makes a resume a guess, and a wrong resume is the single thing
/// that tells a student the tutor was never really following along.
///
/// It is three-dimensional on purpose:
/// <list type="bullet">
/// <item><b>Script node</b> — which beat of the lesson.</item>
/// <item><b>Utterance offset</b> — how far into that beat's speech, so a barge-in can be
/// undone without restarting the sentence.</item>
/// <item><b>Canvas state</b> — what was on screen. Restoring the speech but not the diagram
/// is still a wrong resume; the student is looking at the wrong picture while the right
/// words arrive.</item>
/// </list>
/// </summary>
public sealed record ResumePointer(Guid LessonId, Guid ScriptNodeId, int UtteranceOffset, string CanvasState)
{
    public const string NoCanvas = "none";

    public static ResumePointer AtStartOf(Guid lessonId, ScriptNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new ResumePointer(lessonId, node.Id, 0, CanvasStateOf(node));
    }

    public bool IsMidUtterance => UtteranceOffset > 0;

    /// <summary>
    /// Records exactly where the tutor was cut off. This is the truth about what the student
    /// heard, so it is stored verbatim — deciding where to re-enter is a separate job, and it
    /// belongs to <see cref="UtteranceBoundary"/>.
    /// </summary>
    public ResumePointer HeldAt(int utteranceOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(utteranceOffset);
        return this with { UtteranceOffset = utteranceOffset };
    }

    public static string CanvasStateOf(ScriptNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.VisualRef is null
            ? NoCanvas
            : $"{node.Kind.ToString().ToLowerInvariant()}:{node.VisualRef}";
    }
}
