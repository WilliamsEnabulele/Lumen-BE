namespace Lumen.Domain.Courses;

/// <summary>
/// Orders a course's concepts so prerequisites come first, and refuses to order one that
/// contains a cycle.
///
/// The refusal is the important half. A generated lesson graph can easily contain a cycle —
/// two concepts that each explain themselves in terms of the other — and a course published
/// in an impossible order teaches every student who takes it the wrong thing in the wrong
/// sequence. So this is a publish gate, not a hint: a cycle is reported and named, never
/// silently broken by dropping an edge.
/// </summary>
public static class PrerequisiteGraph
{
    public static ConceptOrdering Order(
        IReadOnlyCollection<string> conceptKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> prerequisites)
    {
        var keys = conceptKeys.Distinct().ToArray();
        var indegree = new Dictionary<string, int>(keys.Length);
        foreach (var key in keys) indegree[key] = 0;

        foreach (var key in keys)
            foreach (var prerequisite in Required(prerequisites, key))
                if (indegree.ContainsKey(prerequisite))
                    indegree[key]++;

        // Deterministic: the same graph must always produce the same order, or two students
        // taking the same course are not taking the same course.
        var ready = new PriorityQueue<string, string>();
        foreach (var (key, degree) in indegree)
            if (degree == 0) ready.Enqueue(key, key);

        var ordered = new List<string>(keys.Length);
        while (ready.Count > 0)
        {
            var key = ready.Dequeue();
            ordered.Add(key);

            foreach (var dependent in keys)
            {
                if (indegree[dependent] <= 0) continue;               // already emitted, or not waiting on this
                if (!Required(prerequisites, dependent).Contains(key)) continue;
                if (--indegree[dependent] == 0) ready.Enqueue(dependent, dependent);
            }
        }

        var cyclic = keys.Where(key => indegree[key] > 0).OrderBy(key => key, StringComparer.Ordinal).ToArray();
        return new ConceptOrdering(ordered, cyclic);
    }

    private static IReadOnlyList<string> Required(
        IReadOnlyDictionary<string, IReadOnlyList<string>> prerequisites, string key) =>
        prerequisites.TryGetValue(key, out var required) ? required : [];
}

public sealed record ConceptOrdering(IReadOnlyList<string> Ordered, IReadOnlyList<string> CyclicConceptKeys)
{
    /// <summary>True when the graph cannot be taught in any order. Blocks publication.</summary>
    public bool HasCycle => CyclicConceptKeys.Count > 0;
}
