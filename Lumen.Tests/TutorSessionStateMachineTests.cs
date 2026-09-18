using Lumen.Domain.Scripts;
using Lumen.Domain.Sessions;

namespace Lumen.Tests;

/// <summary>
/// The invariant that is the product: teaching is never re-entered without a resume pointer.
/// It lives in the domain so no new caller can route around it and quietly resume from the
/// top of the lesson.
/// </summary>
public class TutorSessionStateMachineTests
{
    private static readonly Guid Lesson = Guid.Parse("0197b9c2-0000-7000-8000-00000000000a");

    private static ResumePointer SomewhereInTheLesson() =>
        new(Lesson, Guid.Parse("0197b9c2-0000-7000-8000-00000000000b"), 42, "code:loops-nested");

    [Theory]
    [InlineData(TutorState.Listening)]
    [InlineData(TutorState.Answering)]
    [InlineData(TutorState.CheckingUnderstanding)]
    [InlineData(TutorState.Adapting)]
    [InlineData(TutorState.Paused)]
    public void Teaching_cannot_be_resumed_without_a_pointer(TutorState from)
    {
        var refusal = TutorSessionStateMachine.Validate(from, TutorState.Teaching, pointer: null);

        Assert.NotNull(refusal);
        Assert.Contains("resume pointer", refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(TutorState.Listening)]
    [InlineData(TutorState.Answering)]
    [InlineData(TutorState.Adapting)]
    [InlineData(TutorState.Paused)]
    public void Teaching_resumes_when_the_pointer_is_there(TutorState from)
    {
        Assert.Null(TutorSessionStateMachine.Validate(from, TutorState.Teaching, SomewhereInTheLesson()));
    }

    [Fact]
    public void Leaving_teaching_without_capturing_where_we_were_is_refused()
    {
        var refusal = TutorSessionStateMachine.Validate(TutorState.Teaching, TutorState.Listening, pointer: null);

        Assert.NotNull(refusal);
        Assert.Contains("capturing a resume pointer", refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_first_lesson_starts_teaching_without_needing_a_pointer()
    {
        // Arriving from LoadingLesson is a start, not a resumption.
        Assert.Null(TutorSessionStateMachine.Validate(TutorState.LoadingLesson, TutorState.Teaching, pointer: null));
    }

    [Theory]
    [InlineData(TutorState.Idle, TutorState.Teaching)]
    [InlineData(TutorState.Idle, TutorState.Answering)]
    [InlineData(TutorState.LessonComplete, TutorState.Teaching)]
    [InlineData(TutorState.Teaching, TutorState.Adapting)]
    public void Transitions_that_are_not_in_the_machine_are_refused(TutorState from, TutorState to)
    {
        var refusal = TutorSessionStateMachine.Validate(from, to, SomewhereInTheLesson());

        Assert.NotNull(refusal);
        Assert.Contains("Cannot move", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_session_refuses_the_move_rather_than_making_it()
    {
        var session = new TutorSession { State = TutorState.Teaching };

        var error = Assert.Throws<InvalidOperationException>(
            () => session.MoveTo(TutorState.Listening, pointer: null));

        Assert.Contains("resume pointer", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TutorState.Teaching, session.State);
    }

    [Fact]
    public void A_detour_and_back_lands_on_the_captured_pointer()
    {
        var held = SomewhereInTheLesson();
        var session = new TutorSession { State = TutorState.Teaching, Pointer = held };

        session.MoveTo(TutorState.Listening, held);
        session.MoveTo(TutorState.Answering, pointer: null);
        session.MoveTo(TutorState.Teaching, pointer: null);

        Assert.Equal(TutorState.Teaching, session.State);
        Assert.Equal(held, session.Pointer);
    }

    [Fact]
    public void A_pointer_survives_a_pause_and_a_device_change()
    {
        var held = SomewhereInTheLesson();
        var session = new TutorSession { State = TutorState.Teaching, Pointer = held };

        session.MoveTo(TutorState.Paused, held);

        // A different device, a different day: the state and the pointer are all it takes.
        var elsewhere = new TutorSession { State = session.State, Pointer = session.Pointer };
        elsewhere.MoveTo(TutorState.Teaching, pointer: null);

        Assert.Equal(TutorState.Teaching, elsewhere.State);
        Assert.Equal(42, elsewhere.Pointer?.UtteranceOffset);
        Assert.Equal("code:loops-nested", elsewhere.Pointer?.CanvasState);
    }
}
