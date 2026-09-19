using Lumen.Domain.Canvas;

namespace Lumen.Domain.Teaching;

/// <summary>Who said a thing, in the running conversation the tutor can see.</summary>
public enum Speaker
{
    Tutor,
    Student
}

public sealed record TutorTurn(Speaker Speaker, string Text, DateTimeOffset At)
{
    public static TutorTurn FromTutor(string text) => new(Speaker.Tutor, text, DateTimeOffset.UtcNow);

    public static TutorTurn FromStudent(string text) => new(Speaker.Student, text, DateTimeOffset.UtcNow);
}

/// <summary>
/// Everything the tutor knows when it opens its mouth.
///
/// The canvas is in here on purpose. A tutor that cannot see what the student is looking at
/// will say "as you can see here" about a diagram it cleared two turns ago, and that single
/// tell does more damage to the illusion of being taught than a wrong fact would.
/// </summary>
public sealed record TutorContext(
    string CourseTitle,
    PlannedLesson Lesson,
    PlannedConcept Concept,
    CanvasState Canvas,
    RegisterLevel Register,
    IReadOnlyList<TutorTurn> History)
{
    /// <summary>How many past turns to carry. Enough to stay coherent, bounded so cost is too.</summary>
    public const int HistoryWindow = 16;

    public IReadOnlyList<TutorTurn> RecentHistory =>
        History.Count <= HistoryWindow ? History : History.TakeLast(HistoryWindow).ToArray();
}

/// <summary>Why the tutor is being asked to say something.</summary>
public enum TutorIntent
{
    /// <summary>Carry on teaching the current concept.</summary>
    Teach,

    /// <summary>The student cut in and said something. Deal with it.</summary>
    Respond,

    /// <summary>Check they have it before moving on.</summary>
    CheckUnderstanding,

    /// <summary>They did not have it. Come at it from a different angle.</summary>
    Reteach
}

/// <summary>What came back from a turn: what was said, and what went on the canvas.</summary>
public sealed record TutorResponse(
    string Said,
    IReadOnlyList<CanvasCommand> Drew,
    /// <summary>True when the tutor judges this concept taught and is ready to move on.</summary>
    bool ConceptComplete,
    /// <summary>Canvas calls that were refused, for logs — never shown to the student.</summary>
    IReadOnlyList<string> Refusals)
{
    public static readonly TutorResponse Silent = new(string.Empty, [], false, []);
}

/// <summary>
/// Generates what the tutor says next, and what it draws while saying it.
///
/// One call per conversational turn rather than one per lesson: the tutor is answering a
/// specific student at a specific moment, so nothing useful can be prepared in advance beyond
/// the plan.
/// </summary>
public interface ITutorBrain
{
    string Name { get; }

    Task<TutorResponse> RespondAsync(
        TutorContext context,
        TutorIntent intent,
        string? studentSaid,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Tools the tutor has that do not draw anything.
///
/// Concept completion is a tool call rather than something parsed out of the prose because
/// "I think it has finished explaining" is not a judgement worth making with a regex — and a
/// lesson that advances on a false positive skips material the student never heard.
///
/// What the call means is deliberately weaker than its name suggests. The tutor knows what it
/// has said; it does not know what the student can do. So this is a request to move on, and the
/// answer to a check is what actually grants it.
/// </summary>
public static class TutorControlTools
{
    public const string ConceptTaught = "concept_taught";

    public const string ConceptTaughtDescription =
        "Call this when you think the student has what they need on the current concept. It does "
        + "not end the concept — it brings forward the question that checks whether you are right, "
        + "and they move on when they answer it well. Say your last sentence on the concept in the "
        + "same turn. Do not call it just because you have spoken a few times.";

    public const string ConceptTaughtSchema =
        """{"type":"object","properties":{},"additionalProperties":false}""";
}
