using Lumen.Domain.Canvas;
using Lumen.Domain.Common;

namespace Lumen.Domain.Teaching;

/// <summary>
/// A student's seat in a course, as the server holds it.
///
/// The resume pointer survives the move to conversational teaching, it just changes shape:
/// where it used to be a script node and an offset, it is now a position in the plan plus the
/// canvas and the conversation so far. The principle is unchanged and is the reason this is a
/// persisted object rather than something inferred — resumption is restored, never guessed.
/// </summary>
public sealed class TeachingSession : Entity
{
    public Guid StudentId { get; set; }
    public Guid CourseId { get; set; }

    public int LessonIndex { get; set; }
    public int ConceptIndex { get; set; }

    public CanvasState Canvas { get; set; } = CanvasState.Empty;
    public RegisterLevel Register { get; set; } = RegisterLevel.StandardEnglish;

    public List<TutorTurn> History { get; set; } = [];

    /// <summary>Turns spent on the current concept, which is how a check is timed.</summary>
    public int TurnsOnConcept { get; set; }

    /// <summary>Set when the last check came back wrong, so the next turn reteaches.</summary>
    public bool AwaitingReteach { get; set; }

    /// <summary>Set once a check has been asked, so the next student utterance is read as an answer.</summary>
    public bool AwaitingCheckAnswer { get; set; }

    public bool Complete { get; set; }

    public PlannedLesson? CurrentLesson(LessonPlan plan) =>
        LessonIndex >= 0 && LessonIndex < plan.Lessons.Count ? plan.Lessons[LessonIndex] : null;

    public PlannedConcept? CurrentConcept(LessonPlan plan)
    {
        var lesson = CurrentLesson(plan);
        if (lesson is null) return null;
        return ConceptIndex >= 0 && ConceptIndex < lesson.Concepts.Count ? lesson.Concepts[ConceptIndex] : null;
    }

    public void Record(TutorTurn turn)
    {
        History.Add(turn);
        if (turn.Speaker == Speaker.Tutor) TurnsOnConcept++;
    }

    /// <summary>Moves to the next concept, then the next lesson, then finishes.</summary>
    public void Advance(LessonPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        TurnsOnConcept = 0;
        AwaitingReteach = false;
        AwaitingCheckAnswer = false;

        var lesson = CurrentLesson(plan);
        if (lesson is not null && ConceptIndex + 1 < lesson.Concepts.Count)
        {
            ConceptIndex++;
            return;
        }

        if (LessonIndex + 1 < plan.Lessons.Count)
        {
            LessonIndex++;
            ConceptIndex = 0;
            return;
        }

        Complete = true;
    }
}
