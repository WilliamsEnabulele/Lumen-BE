using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Lumen.Domain.Ingestion;
using Lumen.Domain.Teaching;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Ai;

/// <summary>
/// Reads the document and decides what it teaches.
///
/// Structured output rather than "please reply in JSON": the plan has to parse, and a malformed
/// reply here fails an upload the student is waiting on. The schema is the contract.
/// </summary>
public sealed class AnthropicLessonAuthor(
    AnthropicClient client,
    AnthropicOptions options,
    ILogger<AnthropicLessonAuthor> logger) : ILessonAuthor
{
    public string Name => $"anthropic:{options.AuthorModel}";

    public async Task<LessonPlan> AuthorAsync(
        ExtractedDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = options.AuthorModel,
            MaxTokens = options.AuthorMaxTokens,
            System = AuthorPrompt.System,
            Messages = [new() { Role = Role.User, Content = AuthorPrompt.User(document) }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = JsonSchemas.Whole(AuthorPrompt.PlanSchema) },
            },
        }, cancellationToken);

        var json = string.Concat(response.Content
            .Select(block => block.TryPickText(out var text) ? text.Text : string.Empty));

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("The lesson author returned nothing to parse.");

        logger.LogInformation("Authored a plan from {Words} words of {Title}.", document.WordCount, document.Title);
        return Parse(json, document.Title);
    }

    internal static LessonPlan Parse(string json, string fallbackTitle)
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
