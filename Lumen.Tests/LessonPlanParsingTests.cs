using Lumen.Domain.Ingestion;
using Lumen.Domain.Teaching;
using Lumen.Infrastructure.Ai;

namespace Lumen.Tests;

/// <summary>
/// The plan is what the model decided the document teaches, so parsing it is the seam where a
/// model's output becomes something a student is taught from. What it drops matters as much as
/// what it keeps.
/// </summary>
public class LessonPlanParsingTests
{
    private const string Plan = """
    {
      "course_title": "Introduction to Programming",
      "summary": "How loops work and what nesting them costs.",
      "lessons": [{
        "title": "Loops",
        "objective": "Work out the cost of a nested loop",
        "concepts": [{
          "title": "A loop repeats work",
          "teaching_intent": "Get across that the count is the interesting part, not the body.",
          "prerequisites": [],
          "source_ref": "ch4:p61",
          "source_excerpt": "A loop is a promise to do the same work more than once.",
          "visual_hint": null
        },{
          "title": "Nesting multiplies",
          "teaching_intent": "Show that the counts multiply rather than add.",
          "prerequisites": ["A loop repeats work"],
          "source_ref": "ch4:p63",
          "source_excerpt": "The outer loop takes ten turns, and each one runs the inner loop in full.",
          "visual_hint": "a diagram of the outer loop containing the inner"
        }]
      }]
    }
    """;

    [Fact]
    public void A_plan_becomes_lessons_and_concepts()
    {
        var plan = LessonPlanReader.Read(Plan, "fallback");

        Assert.Equal("Introduction to Programming", plan.CourseTitle);
        Assert.Single(plan.Lessons);
        Assert.Equal(2, plan.ConceptCount);
        Assert.Equal("Work out the cost of a nested loop", plan.Lessons[0].Objective);
    }

    [Fact]
    public void Every_concept_carries_the_passage_the_tutor_is_grounded_in()
    {
        var plan = LessonPlanReader.Read(Plan, "fallback");

        Assert.All(plan.Lessons.SelectMany(lesson => lesson.Concepts),
            concept => Assert.False(string.IsNullOrWhiteSpace(concept.SourceExcerpt)));
    }

    [Fact]
    public void Prerequisites_and_visual_hints_survive()
    {
        var plan = LessonPlanReader.Read(Plan, "fallback");
        var nesting = plan.Lessons[0].Concepts[1];

        Assert.Equal(["A loop repeats work"], nesting.Prerequisites);
        Assert.Equal("a diagram of the outer loop containing the inner", nesting.VisualHint);
        Assert.Null(plan.Lessons[0].Concepts[0].VisualHint);
    }

    [Fact]
    public void A_concept_with_nothing_to_teach_from_is_dropped_rather_than_taught()
    {
        // Without an excerpt the tutor falls back on its own general knowledge, which is exactly
        // what grounding exists to prevent. Better to lose the concept than to teach ungrounded.
        var plan = LessonPlanReader.Read("""
        {"course_title":"C","summary":"","lessons":[{"title":"L","objective":"O","concepts":[
          {"title":"Real","teaching_intent":"t","prerequisites":[],"source_ref":"p1","source_excerpt":"Something real.","visual_hint":null},
          {"title":"Hollow","teaching_intent":"t","prerequisites":[],"source_ref":"p2","source_excerpt":"","visual_hint":null}
        ]}]}
        """, "fallback");

        Assert.Equal(1, plan.ConceptCount);
        Assert.Equal("Real", plan.Lessons[0].Concepts[0].Title);
    }

    [Fact]
    public void A_lesson_left_with_no_concepts_is_dropped_too()
    {
        var plan = LessonPlanReader.Read("""
        {"course_title":"C","summary":"","lessons":[{"title":"Empty","objective":"O","concepts":[]}]}
        """, "fallback");

        Assert.Empty(plan.Lessons);
    }

    [Fact]
    public void A_plan_with_no_title_falls_back_to_the_documents()
    {
        var plan = LessonPlanReader.Read("""{"course_title":"","summary":"","lessons":[]}""", "My Document");

        Assert.Equal("My Document", plan.CourseTitle);
    }
}

/// <summary>
/// With no model configured the product is in a degraded mode, and the tests say so — this is
/// what keeps the upload path runnable for anyone who clones the repository without a key.
/// </summary>
public class FallbackTeachingTests
{
    private static ExtractedDocument Document() => new("Notes", [
        new ExtractedSection("Loops", 2, [
            new ExtractedBlock(BlockKind.Paragraph, "A loop is a promise to do the same work more than once.", "p1"),
        ]),
        new ExtractedSection("Nesting", 2, [
            new ExtractedBlock(BlockKind.Paragraph, "The outer loop runs the inner loop in full each turn.", "p2"),
            new ExtractedBlock(BlockKind.Code, "for i in range(3): pass", "p3"),
        ]),
    ]);

    private static TutorContext Context(PlannedConcept concept, params TutorTurn[] history) => new(
        "Notes", new PlannedLesson("Loops", "Understand loops", [concept]), concept,
        Lumen.Domain.Canvas.CanvasState.Empty, RegisterLevel.StandardEnglish, history);

    [Fact]
    public async Task The_deterministic_author_follows_the_documents_own_headings()
    {
        var plan = await new DeterministicLessonAuthor().AuthorAsync(Document());

        Assert.Equal(2, plan.Lessons.Count);
        Assert.Equal(["Loops"], plan.Lessons[1].Concepts[0].Prerequisites);
        Assert.NotNull(plan.Lessons[1].Concepts[0].VisualHint);
    }

    [Fact]
    public async Task The_scripted_tutor_admits_it_cannot_answer_rather_than_guessing()
    {
        var plan = await new DeterministicLessonAuthor().AuthorAsync(Document());
        var concept = plan.Lessons[0].Concepts[0];

        var response = await new ScriptedTutorBrain()
            .RespondAsync(Context(concept), TutorIntent.Respond, "why does that multiply?");

        Assert.Contains("cannot answer", response.Said, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.ConceptComplete);
    }

    [Fact]
    public async Task The_scripted_tutor_reads_the_material_and_then_stops()
    {
        var plan = await new DeterministicLessonAuthor().AuthorAsync(Document());
        var concept = plan.Lessons[0].Concepts[0];
        var brain = new ScriptedTutorBrain();

        var first = await brain.RespondAsync(Context(concept), TutorIntent.Teach, null);

        Assert.False(string.IsNullOrWhiteSpace(first.Said));
        Assert.Single(first.Drew);

        // Once it has read everything it has, it says so by completing rather than repeating.
        var afterEverything = await brain.RespondAsync(
            Context(concept, TutorTurn.FromTutor(first.Said), TutorTurn.FromTutor("more")),
            TutorIntent.Teach, null);

        Assert.True(afterEverything.ConceptComplete);
    }
}
