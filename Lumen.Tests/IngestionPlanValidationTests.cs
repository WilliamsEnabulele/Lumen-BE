using Lumen.Domain.Teaching;

namespace Lumen.Tests;

/// <summary>
/// A model can write a plan whose prerequisites form a loop. Publishing one would mis-teach
/// everyone who took the course, in an order nobody could complete, so the upload is refused
/// rather than the edge silently dropped.
/// </summary>
public class IngestionPlanValidationTests
{
    private static PlannedConcept Concept(string title, params string[] prerequisites) =>
        new(title, "intent", prerequisites, "p1", "something to teach from", null);

    [Fact]
    public void A_sound_plan_passes()
    {
        var plan = new LessonPlan("Course", "", [
            new PlannedLesson("One", "objective", [
                Concept("Loops"),
                Concept("Nesting", "Loops"),
            ]),
        ]);

        Assert.Null(LessonPlanValidator.CycleIn(plan));
    }

    [Fact]
    public void A_plan_that_doubles_back_is_caught_and_named()
    {
        var plan = new LessonPlan("Course", "", [
            new PlannedLesson("One", "objective", [
                Concept("Chicken", "Egg"),
                Concept("Egg", "Chicken"),
            ]),
        ]);

        var cycle = LessonPlanValidator.CycleIn(plan);

        Assert.NotNull(cycle);
        Assert.Contains(cycle, new[] { "Chicken", "Egg" });
    }

    [Fact]
    public void A_prerequisite_taught_in_an_earlier_lesson_is_not_a_cycle()
    {
        // Ordering is checked within a lesson; a reference across lessons is a review question,
        // not something to refuse an upload over.
        var plan = new LessonPlan("Course", "", [
            new PlannedLesson("One", "objective", [Concept("Loops")]),
            new PlannedLesson("Two", "objective", [Concept("Nesting", "Loops")]),
        ]);

        Assert.Null(LessonPlanValidator.CycleIn(plan));
    }
}
