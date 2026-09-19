using Lumen.Domain.Courses;

namespace Lumen.Domain.Teaching;

/// <summary>
/// Whether a plan can actually be taught.
///
/// A model can write prerequisites that form a loop — two concepts that each explain
/// themselves in terms of the other. There is no order in which such a course can be taught,
/// so it is refused at upload rather than published and discovered by the first student.
///
/// Ordering is checked within a lesson. A concept referring to something taught in an earlier
/// lesson is a review question, not grounds for refusing someone's upload.
/// </summary>
public static class LessonPlanValidator
{
    /// <summary>Returns the title of a concept caught in a cycle, or null when the plan is sound.</summary>
    public static string? CycleIn(LessonPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var lesson in plan.Lessons)
        {
            var titles = lesson.Concepts.Select(concept => concept.Title).ToArray();
            var prerequisites = lesson.Concepts
                .GroupBy(concept => concept.Title, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group.First().Prerequisites.ToArray(),
                    StringComparer.Ordinal);

            var ordering = PrerequisiteGraph.Order(titles, prerequisites);
            if (ordering.HasCycle) return ordering.CyclicConceptKeys[0];
        }

        return null;
    }
}
