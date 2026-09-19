using Lumen.Domain.Assessment;
using Lumen.Domain.Teaching;

namespace Lumen.Tests;

/// <summary>
/// What happens the moment a concept is entered. The rule being protected here is that a
/// concept is skipped on evidence and on nothing else — not on a prior, not on a guess, and
/// not on the tutor's opinion of how the last one went.
/// </summary>
public class ConceptEntryTests
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

    /// <summary>A record answered into correctly twice, which is what mastery costs from cold.</summary>
    private static MasteryRecord Demonstrated(string title)
    {
        var record = new MasteryRecord { ConceptTitle = title, ConceptKey = title };
        record.Record(new AnswerJudgement(Verdict.Correct, null), "q", "a");
        record.Record(new AnswerJudgement(Verdict.Correct, null), "q", "a");
        return record;
    }

    private static Func<PlannedConcept, MasteryRecord?> Known(params MasteryRecord[] records) =>
        concept => records.FirstOrDefault(record => record.ConceptTitle == concept.Title);

    [Fact]
    public void A_concept_already_demonstrated_is_not_taught_again()
    {
        var plan = Plan();
        var session = new TeachingSession();

        var skipped = ConceptEntry.SkipKnown(session, plan, Known(Demonstrated("A")));

        Assert.Equal(new[] { "A" }, skipped);
        Assert.Equal("B", session.CurrentConcept(plan)?.Title);
    }

    [Fact]
    public void Skipping_runs_on_until_it_finds_something_worth_teaching()
    {
        var plan = Plan();
        var session = new TeachingSession();

        var skipped = ConceptEntry.SkipKnown(session, plan, Known(Demonstrated("A"), Demonstrated("B")));

        Assert.Equal(new[] { "A", "B" }, skipped);
        Assert.Equal("C", session.CurrentConcept(plan)?.Title);
        Assert.Equal("Two", session.CurrentLesson(plan)?.Title);
    }

    [Fact]
    public void A_student_who_knows_the_whole_course_is_finished_rather_than_stuck()
    {
        var plan = Plan();
        var session = new TeachingSession();

        var skipped = ConceptEntry.SkipKnown(
            session, plan, Known(Demonstrated("A"), Demonstrated("B"), Demonstrated("C")));

        Assert.Equal(3, skipped.Count);
        Assert.True(session.Complete);
    }

    [Fact]
    public void Nothing_is_skipped_on_a_record_nobody_ever_answered_into()
    {
        // The dangerous case. A cold prior is not knowledge, and a belief number that has never
        // seen an answer must never let someone past the material.
        var plan = Plan();
        var session = new TeachingSession();
        var untouched = new MasteryRecord { ConceptTitle = "A", ConceptKey = "A", Belief = 0.99 };

        Assert.Empty(ConceptEntry.SkipKnown(session, plan, Known(untouched)));
        Assert.Equal("A", session.CurrentConcept(plan)?.Title);
    }

    [Fact]
    public void Nothing_is_skipped_once_the_concept_is_under_way()
    {
        // Belief can tick over mid-explanation. That is not a reason to abandon the sentence.
        var plan = Plan();
        var session = new TeachingSession { TurnsOnConcept = 2 };

        Assert.Empty(ConceptEntry.SkipKnown(session, plan, Known(Demonstrated("A"))));
        Assert.Equal("A", session.CurrentConcept(plan)?.Title);
    }

    [Fact]
    public void A_concept_answered_into_but_not_mastered_is_still_taught()
    {
        var plan = Plan();
        var session = new TeachingSession();
        var shaky = new MasteryRecord { ConceptTitle = "A", ConceptKey = "A" };
        shaky.Record(new AnswerJudgement(Verdict.Partial, "half of it"), "q", "a");

        Assert.Empty(ConceptEntry.SkipKnown(session, plan, Known(shaky)));
    }

    [Fact]
    public void A_finished_session_is_left_alone()
    {
        var plan = Plan();
        var session = new TeachingSession { Complete = true, LessonIndex = 99 };

        Assert.Empty(ConceptEntry.SkipKnown(session, plan, Known(Demonstrated("A"))));
    }
}
