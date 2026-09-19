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

    public static TutorIntent Decide(TeachingSession session, string? studentSaid)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Whatever else was planned, a student who just spoke gets answered first. Carrying on
        // with a scheduled check over the top of a question is the single rudest thing a tutor
        // can do, and it teaches them not to interrupt.
        if (!string.IsNullOrWhiteSpace(studentSaid)) return TutorIntent.Respond;

        if (session.AwaitingReteach) return TutorIntent.Reteach;

        if (session.TurnsOnConcept >= TurnsBeforeCheck && !session.AwaitingCheckAnswer)
            return TutorIntent.CheckUnderstanding;

        return TutorIntent.Teach;
    }
}
