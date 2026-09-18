namespace Lumen.Domain.Sessions;

/// <summary>
/// The allowed transitions, in one place.
///
/// One invariant here is not a convenience — it is the product: <b>teaching cannot be
/// re-entered without a resume pointer.</b> Every path back into Teaching comes from a detour
/// (a question, a comprehension check, a pause, possibly on another device days later), and
/// each one is a chance to come back in the wrong place. Putting the check in the domain
/// rather than in the session service means no new caller can route around it and quietly
/// resume from the top of the lesson.
/// </summary>
public static class TutorSessionStateMachine
{
    private static readonly Dictionary<TutorState, TutorState[]> Allowed = new()
    {
        [TutorState.Idle]                  = [TutorState.LoadingLesson],
        [TutorState.LoadingLesson]         = [TutorState.Teaching, TutorState.Paused, TutorState.Idle],
        // Listening is reachable from Teaching and from a comprehension check alike: a student
        // may answer the question, or may interrupt to ask what the question means.
        [TutorState.Teaching]              = [TutorState.Listening, TutorState.CheckingUnderstanding, TutorState.LessonComplete, TutorState.Paused],
        [TutorState.Listening]             = [TutorState.Answering, TutorState.Teaching, TutorState.CheckingUnderstanding, TutorState.Paused],
        // Answering back to Listening: an answer that prompts a follow-up is the normal case,
        // not an edge one, and routing it through Teaching first would speak a sentence of
        // lesson nobody asked for between the two halves of one exchange.
        [TutorState.Answering]             = [TutorState.Teaching, TutorState.Listening, TutorState.Paused],
        [TutorState.CheckingUnderstanding] = [TutorState.Adapting, TutorState.Teaching, TutorState.Listening, TutorState.Paused],
        [TutorState.Adapting]              = [TutorState.Teaching, TutorState.Paused],
        [TutorState.Paused]                = [TutorState.Teaching, TutorState.LoadingLesson, TutorState.Idle],
        [TutorState.LessonComplete]        = [TutorState.LoadingLesson, TutorState.Idle]
    };

    /// <summary>States from which arriving at Teaching is a resumption rather than a start.</summary>
    private static readonly TutorState[] ResumesTeaching =
        [TutorState.Listening, TutorState.Answering, TutorState.CheckingUnderstanding, TutorState.Adapting, TutorState.Paused];

    public static bool CanTransition(TutorState from, TutorState to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>
    /// Returns null when the transition is permitted, or the reason it is refused.
    /// Callers must treat a non-null result as a hard stop.
    /// </summary>
    public static string? Validate(TutorState from, TutorState to, ResumePointer? pointer)
    {
        if (!CanTransition(from, to))
            return $"Cannot move a tutor session from {from} to {to}.";

        if (to == TutorState.Teaching && ResumesTeaching.Contains(from) && pointer is null)
            return $"Cannot resume teaching from {from} without a resume pointer. " +
                   "Resumption is restored from the pointer, never inferred from the conversation.";

        // Leaving Teaching for a detour without recording where we were is the same bug one
        // step earlier: by the time the detour ends there is nothing left to come back to.
        if (from == TutorState.Teaching && to is TutorState.Listening or TutorState.Paused && pointer is null)
            return $"Cannot leave Teaching for {to} without capturing a resume pointer first.";

        return null;
    }
}
