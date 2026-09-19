using Lumen.Domain.Canvas;
using Lumen.Domain.Ingestion;
using Lumen.Domain.Teaching;

namespace Lumen.Infrastructure.Ai;

/// <summary>
/// What happens with no model configured.
///
/// This is a degraded mode, not an alternative implementation, and it is named that way on
/// purpose. It keeps the upload path runnable for anyone who clones the repository without a
/// key — a flow nobody can run is a flow nobody checks — but it cannot converse, and it says so
/// rather than pretending.
/// </summary>
public sealed class DeterministicLessonAuthor : ILessonAuthor
{
    public string Name => "deterministic (no model configured)";

    public Task<LessonPlan> AuthorAsync(ExtractedDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var lessons = new List<PlannedLesson>();
        string? previousTitle = null;

        foreach (var section in document.Sections)
        {
            var body = string.Join(' ', section.Blocks
                .Where(block => block.Kind is BlockKind.Paragraph or BlockKind.ListItem)
                .Select(block => block.Text));

            if (string.IsNullOrWhiteSpace(body)) continue;

            var title = string.IsNullOrWhiteSpace(section.Heading) ? "Untitled" : section.Heading.Trim();
            var code = section.Blocks.FirstOrDefault(block => block.Kind == BlockKind.Code);

            var concept = new PlannedConcept(
                Title: title,
                TeachingIntent: $"Cover {title} as the document states it.",
                Prerequisites: previousTitle is null ? [] : [previousTitle],
                SourceRef: section.Blocks.FirstOrDefault()?.SourceRef ?? "unknown",
                SourceExcerpt: body,
                VisualHint: code is null ? null : "the code sample from this section");

            lessons.Add(new PlannedLesson(title, $"Understand {title}", [concept]));
            previousTitle = title;
        }

        return Task.FromResult(new LessonPlan(
            document.Title,
            "Structured without a model, so the outline follows the document's own headings.",
            lessons));
    }
}

/// <summary>
/// A tutor with no model behind it. It reads the material out in order and admits, when asked
/// anything, that it cannot answer without one — which is the honest version of this state.
/// </summary>
public sealed class ScriptedTutorBrain : ITutorBrain
{
    public string Name => "scripted (no model configured)";

    public Task<TutorResponse> RespondAsync(
        TutorContext context,
        TutorIntent intent,
        string? studentSaid,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (intent is TutorIntent.Respond or TutorIntent.CheckUnderstanding or TutorIntent.Reteach)
        {
            return Task.FromResult(new TutorResponse(
                "I can read this material out, but I cannot answer questions about it — there is no "
                + "model configured, so there is nothing here that understands what you just asked.",
                [], ConceptComplete: false, Refusals: []));
        }

        // Read the concept out in beats the size the resume pointer expects.
        var said = context.History.Count(turn => turn.Speaker == Speaker.Tutor);
        var beats = Utterances.Split(context.Concept.SourceExcerpt);

        if (said >= beats.Count)
            return Task.FromResult(new TutorResponse(string.Empty, [], ConceptComplete: true, Refusals: []));

        var drew = said == 0
            ? new CanvasCommand[] { new ShowStatement(Trim(context.Concept.Title)) }
            : [];

        return Task.FromResult(new TutorResponse(
            beats[said], drew, ConceptComplete: said == beats.Count - 1, Refusals: []));
    }

    private static string Trim(string text) =>
        text.Length <= CanvasTools.MaxStatementLength ? text : text[..CanvasTools.MaxStatementLength];
}
