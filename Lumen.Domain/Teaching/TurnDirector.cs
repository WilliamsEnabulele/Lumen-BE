namespace Lumen.Domain.Teaching;

/// <summary>
/// Decides what the tutor is doing this turn.
///
/// Kept out of the model on purpose. Asking the model to decide both *what to say* and
/// *whether it is time to check understanding* means the check arrives when the prose feels
/// like a good place for one, which is not the same as when the student needs it — and it
/// makes the behaviour impossible to test, because it is buried in a generation.
/// </summary>
public static class TurnDirector
{
    /// <summary>
    /// Tutor turns on one concept before checking they have it. Low enough to catch a
    /// misunderstanding before it compounds, high enough that the concept is actually taught
    /// first rather than quizzed at.
    /// </summary>
    public const int TurnsBeforeCheck = 3;

    /// <summary>
    /// Turns on one concept before the lesson gives up on it and moves on regardless.
    ///
    /// A student who never answers cannot be marked, so nothing else here would ever let them
    /// out — the reteach cap only counts wrong answers. Without this a silent student is stuck
    /// on one concept until they close the tab.
    /// </summary>
    public const int MaxTurnsOnConcept = 12;

    public static TutorIntent Decide(TeachingSession session, string? studentSaid)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Whatever else was planned, a student who just spoke gets answered first. Carrying on
        // with a scheduled check over the top of a question is the single rudest thing a tutor
        // can do, and it teaches them not to interrupt.
        if (!string.IsNullOrWhiteSpace(studentSaid)) return TutorIntent.Respond;

        if (session.AwaitingReteach) return TutorIntent.Reteach;

        // A check already asked is waiting for an answer. Asking a second one over the top of
        // the first is how a lesson turns into an interrogation.
        if (session.AwaitingCheckAnswer) return TutorIntent.Teach;

        // The tutor saying it is done brings the check forward; it does not replace it.
        if (session.TutorSaysReady || session.TurnsOnConcept >= TurnsBeforeCheck)
            return TutorIntent.CheckUnderstanding;

        return TutorIntent.Teach;
    }

    /// <summary>
    /// True when this concept has gone on long enough that continuing is not teaching anyone
    /// anything. The lesson moves on and records that it was never mastered.
    /// </summary>
    public static bool HasStalled(TeachingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.TurnsOnConcept >= MaxTurnsOnConcept;
    }
}
