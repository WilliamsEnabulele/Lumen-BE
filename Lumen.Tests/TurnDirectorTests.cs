using Lumen.Domain.Teaching;

namespace Lumen.Tests;

public class TurnDirectorTests
{
    private static LessonPlan Plan() => new("Course", "", [
        new PlannedLesson("One", "objective", [
            new PlannedConcept("A", "intent", [], "p1", "excerpt a", null),
            new PlannedConcept("B", "intent", ["A"], "p2", "excerpt b", null),
        ]),
        new PlannedLesson("Two", "objective", [
            new PlannedConcept("C", "intent", [], "p3", "excerpt c", null),
        ]),
    ]);

    [Fact]
    public void A_student_who_spoke_is_answered_before_anything_else()
    {
        // Even mid-check. Talking over a question teaches them not to interrupt.
        var session = new TeachingSession { TurnsOnConcept = 99, AwaitingReteach = true };

        Assert.Equal(TutorIntent.Respond, TurnDirector.Decide(session, "wait, why?"));
    }

    [Fact]
    public void Teaching_is_the_default()
    {
        Assert.Equal(TutorIntent.Teach, TurnDirector.Decide(new TeachingSession(), null));
    }

    [Fact]
    public void A_check_arrives_once_the_concept_has_actually_been_taught()
    {
        var session = new TeachingSession { TurnsOnConcept = TurnDirector.TurnsBeforeCheck };

        Assert.Equal(TutorIntent.CheckUnderstanding, TurnDirector.Decide(session, null));
    }

    [Fact]
    public void A_check_is_not_asked_twice_while_waiting_for_the_answer()
    {
        var session = new TeachingSession { TurnsOnConcept = 99, AwaitingCheckAnswer = true };

        Assert.Equal(TutorIntent.Teach, TurnDirector.Decide(session, null));
    }

    [Fact]
    public void A_failed_check_reteaches_rather_than_repeating()
    {
        Assert.Equal(TutorIntent.Reteach, TurnDirector.Decide(new TeachingSession { AwaitingReteach = true }, null));
    }

    [Fact]
    public void Advancing_walks_concepts_then_lessons_then_finishes()
    {
        var plan = Plan();
        var session = new TeachingSession();

        Assert.Equal("A", session.CurrentConcept(plan)?.Title);

        session.Advance(plan);
        Assert.Equal("B", session.CurrentConcept(plan)?.Title);

        session.Advance(plan);
        Assert.Equal("C", session.CurrentConcept(plan)?.Title);
        Assert.Equal("Two", session.CurrentLesson(plan)?.Title);

        session.Advance(plan);
        Assert.True(session.Complete);
    }

    [Fact]
    public void Advancing_resets_what_was_true_of_the_concept_just_left()
    {
        var plan = Plan();
        var session = new TeachingSession { TurnsOnConcept = 5, AwaitingReteach = true, AwaitingCheckAnswer = true };

        session.Advance(plan);

        Assert.Equal(0, session.TurnsOnConcept);
        Assert.False(session.AwaitingReteach);
        Assert.False(session.AwaitingCheckAnswer);
    }

    [Fact]
    public void The_tutor_saying_it_is_done_brings_the_check_forward_rather_than_skipping_it()
    {
        // The whole point of the flag. The model knows what it has said, not what the student
        // can do, so its confidence buys an early question and nothing else.
        var session = new TeachingSession { TurnsOnConcept = 1, TutorSaysReady = true };

        Assert.Equal(TutorIntent.CheckUnderstanding, TurnDirector.Decide(session, null));
    }

    [Fact]
    public void A_tutor_who_says_it_is_done_still_waits_for_the_answer_it_asked_for()
    {
        var session = new TeachingSession { TutorSaysReady = true, AwaitingCheckAnswer = true };

        Assert.Equal(TutorIntent.Teach, TurnDirector.Decide(session, null));
    }

    [Fact]
    public void Advancing_forgets_that_the_tutor_thought_it_was_done()
    {
        var plan = Plan();
        var session = new TeachingSession { TutorSaysReady = true };

        session.Advance(plan);

        Assert.False(session.TutorSaysReady);
    }

    [Fact]
    public void A_concept_stalls_only_once_it_has_gone_on_far_too_long()
    {
        Assert.False(TurnDirector.HasStalled(
            new TeachingSession { TurnsOnConcept = TurnDirector.MaxTurnsOnConcept - 1 }));

        Assert.True(TurnDirector.HasStalled(
            new TeachingSession { TurnsOnConcept = TurnDirector.MaxTurnsOnConcept }));
    }

    [Fact]
    public void A_stall_takes_far_longer_than_a_check()
    {
        // If these ever cross, the concept is abandoned before it is ever assessed.
        Assert.True(TurnDirector.MaxTurnsOnConcept > TurnDirector.TurnsBeforeCheck);
    }

    [Fact]
    public void Only_the_tutors_turns_count_toward_a_check()
    {
        var session = new TeachingSession();

        session.Record(TutorTurn.FromStudent("mm-hm"));
        session.Record(TutorTurn.FromStudent("go on"));

        Assert.Equal(0, session.TurnsOnConcept);
        Assert.Equal(2, session.History.Count);
    }
}
