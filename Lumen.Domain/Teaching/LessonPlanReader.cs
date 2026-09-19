using System.Text.Json;

namespace Lumen.Domain.Teaching;

/// <summary>
/// Reads a model's plan into the shape the rest of the system teaches from.
///
/// This is pure, so it lives with the type it produces rather than inside the client that
/// happened to fetch the JSON — and it is where the decisions about what to *drop* are made,
/// which matter as much as what is kept. A concept with nothing to teach from would send the
/// tutor to its own general knowledge, which is the one thing grounding exists to prevent.
/// </summary>
public static class LessonPlanReader
{
    public static LessonPlan Read(string json, string fallbackTitle)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        var lessons = new List<PlannedLesson>();

        if (root.TryGetProperty("lessons", out var rawLessons) && rawLessons.ValueKind == JsonValueKind.Array)
        {
            foreach (var lesson in rawLessons.EnumerateArray())
            {
                var concepts = new List<PlannedConcept>();

                if (lesson.TryGetProperty("concepts", out var rawConcepts)
                    && rawConcepts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var concept in rawConcepts.EnumerateArray())
                    {
                        var title = Text(concept, "title");
                        var excerpt = Text(concept, "source_excerpt");

                        // A concept with nothing to teach from would send the tutor to its own
                        // general knowledge, which is exactly what grounding exists to prevent.
                        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(excerpt)) continue;

                        concepts.Add(new PlannedConcept(
                            Title: title,
                            TeachingIntent: Text(concept, "teaching_intent"),
                            Prerequisites: Strings(concept, "prerequisites"),
                            SourceRef: Text(concept, "source_ref"),
                            SourceExcerpt: excerpt,
                            VisualHint: Optional(concept, "visual_hint")));
                    }
                }

                if (concepts.Count == 0) continue;

                lessons.Add(new PlannedLesson(
                    Title: Text(lesson, "title"),
                    Objective: Text(lesson, "objective"),
                    Concepts: concepts));
            }
        }

        var courseTitle = Text(root, "course_title");
        return new LessonPlan(
            string.IsNullOrWhiteSpace(courseTitle) ? fallbackTitle : courseTitle,
            Text(root, "summary"),
            lessons);
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? Optional(JsonElement element, string property)
    {
        var text = Text(element, property);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static IReadOnlyList<string> Strings(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }
}
