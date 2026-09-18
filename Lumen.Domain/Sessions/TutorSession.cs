using Lumen.Domain.Common;
using Lumen.Domain.Teaching;

namespace Lumen.Domain.Sessions;

/// <summary>
/// A student's live seat in a course.
///
/// The pointer is checkpointed on every script node boundary rather than at the end of a
/// lesson. At fifteen to thirty seconds a node that is a trivial write rate, and it bounds
/// the worst case — a lost node of progress — to exactly the amount a student would forgive
/// without noticing. It is also what makes this survivable when the node holding the session
/// dies, which it eventually will.
/// </summary>
public sealed class TutorSession : Entity
{
    public Guid StudentId { get; set; }
    public Guid CourseId { get; set; }

    public TutorState State { get; set; } = TutorState.Idle;

    /// <summary>Null only before the first lesson has loaded.</summary>
    public ResumePointer? Pointer { get; set; }

    public RegisterLevel Register { get; set; } = RegisterLevel.StandardEnglish;

    /// <summary>Consecutive code-switches by the student, feeding <see cref="RegisterLadder"/>.</summary>
    public int ConsecutiveStudentCodeSwitches { get; set; }

    public DateTimeOffset? LastInterjectionAt { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Applies a transition, or throws with the domain's own reason. Callers that want to
    /// test a move without performing it use <see cref="TutorSessionStateMachine.Validate"/>.
    /// </summary>
    public void MoveTo(TutorState next, ResumePointer? pointer)
    {
        var refusal = TutorSessionStateMachine.Validate(State, next, pointer ?? Pointer);
        if (refusal is not null) throw new InvalidOperationException(refusal);

        if (pointer is not null) Pointer = pointer;
        State = next;
    }
}
