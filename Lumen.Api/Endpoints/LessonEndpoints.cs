using Lumen.Domain.Retrieval;
using Lumen.Domain.Scripts;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

public sealed record AskRequest(string Question, Guid? CurrentNodeId);

public static class LessonEndpoints
{
    public static void MapLessonEndpoints(this WebApplication app)
    {
        app.MapGet("/api/lessons/{lessonId:guid}/script", (Guid lessonId, ICourseStore store) =>
        {
            var composed = store.List()
                .Select(course => store.Find(course.Id))
                .FirstOrDefault(candidate => candidate?.Lessons.Any(lesson => lesson.Id == lessonId) == true);

            if (composed is null) return Results.NotFound();

            var lesson = composed.Lessons.First(candidate => candidate.Id == lessonId);
            return Results.Ok(new
            {
                lessonId,
                courseId = composed.Course.Id,
                title = lesson.Title,
                nodes = composed.ScriptFor(lessonId).Select(Node),
            });
        });

        app.MapPost("/api/lessons/{lessonId:guid}/ask", (Guid lessonId, AskRequest request, ICourseStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
                return Results.BadRequest(new { error = "No question arrived." });

            var composed = store.List()
                .Select(course => store.Find(course.Id))
                .FirstOrDefault(candidate => candidate?.Lessons.Any(lesson => lesson.Id == lessonId) == true);

            if (composed is null) return Results.NotFound();

            // The current lesson first. Broadening to the rest of the course is the next tier,
            // and it is also the tier where a slightly slower answer is acceptable, because the
            // student has asked something off-lesson.
            var result = LessonRetrieval.Answer(composed.ScriptFor(lessonId), request.Question, request.CurrentNodeId);

            return Results.Ok(new
            {
                text = result.Text,
                sourceNodeId = result.SourceNodeId,
                sourceRef = result.SourceRef,
                inScope = result.InScope,
            });
        });
    }

    private static object Node(ScriptNode node) => new
    {
        id = node.Id,
        ordinal = node.Ordinal,
        kind = node.Kind,
        conceptKey = node.ConceptKey,
        text = node.Text,
        pauseAfterMs = node.PauseAfterMs,
        visualKind = node.VisualKind,
        visualPayload = node.VisualPayload,
        visualRef = node.VisualRef,
        sourceRef = node.SourceRef,
        carriesDefinition = node.CarriesDefinition,
    };
}
