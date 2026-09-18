using Lumen.Domain.Courses;

namespace Lumen.Tests;

/// <summary>
/// A course published in an impossible order teaches every student who takes it the wrong
/// thing in the wrong sequence, so a cycle is named and refused rather than broken silently.
/// </summary>
public class PrerequisiteGraphTests
{
    [Fact]
    public void Prerequisites_come_first()
    {
        var prerequisites = new Dictionary<string, IReadOnlyList<string>>
        {
            ["loops"] = new[] { "variables" },
            ["nesting"] = new[] { "loops" }
        };

        var ordering = PrerequisiteGraph.Order(["nesting", "loops", "variables"], prerequisites);

        Assert.False(ordering.HasCycle);
        Assert.Equal(new[] { "variables", "loops", "nesting" }, ordering.Ordered.ToArray());
    }

    [Fact]
    public void A_cycle_is_reported_and_nothing_in_it_is_ordered()
    {
        var prerequisites = new Dictionary<string, IReadOnlyList<string>>
        {
            ["chicken"] = new[] { "egg" },
            ["egg"] = new[] { "chicken" }
        };

        var ordering = PrerequisiteGraph.Order(["chicken", "egg"], prerequisites);

        Assert.True(ordering.HasCycle);
        Assert.Equal(new[] { "chicken", "egg" }, ordering.CyclicConceptKeys.ToArray());
        Assert.Empty(ordering.Ordered);
    }

    [Fact]
    public void Independent_concepts_are_ordered_deterministically()
    {
        var empty = new Dictionary<string, IReadOnlyList<string>>();

        var first = PrerequisiteGraph.Order(["zebra", "apple", "mango"], empty);
        var second = PrerequisiteGraph.Order(["mango", "zebra", "apple"], empty);

        Assert.Equal(first.Ordered.ToArray(), second.Ordered.ToArray());
        Assert.Equal(new[] { "apple", "mango", "zebra" }, first.Ordered.ToArray());
    }

    [Fact]
    public void A_prerequisite_outside_the_set_does_not_block_the_concept()
    {
        // The student has already finished the prerequisite in an earlier course, so it is
        // not in this ordering. That is not a reason to refuse to teach what depends on it.
        var prerequisites = new Dictionary<string, IReadOnlyList<string>>
        {
            ["loops"] = new[] { "variables-from-another-course" }
        };

        var ordering = PrerequisiteGraph.Order(["loops"], prerequisites);

        Assert.False(ordering.HasCycle);
        Assert.Equal(new[] { "loops" }, ordering.Ordered.ToArray());
    }

    [Fact]
    public void A_concept_listed_twice_is_ordered_once()
    {
        var ordering = PrerequisiteGraph.Order(["loops", "loops"], new Dictionary<string, IReadOnlyList<string>>());

        Assert.Equal(new[] { "loops" }, ordering.Ordered.ToArray());
    }
}
