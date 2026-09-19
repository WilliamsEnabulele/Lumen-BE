using Lumen.Domain.Ingestion;
using Lumen.Infrastructure.Extraction;
using Lumen.Infrastructure.Ingestion;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

public static class CourseEndpoints
{
    /// <summary>
    /// Until there is authentication, everything belongs to one tenant. The column is already
    /// on every row, so switching this for a real claim is a one-line change rather than a
    /// migration through the schema.
    /// </summary>
    private static readonly Guid DefaultTenant = Guid.Parse("0197b9c2-0000-7000-8000-000000000001");

    /// <summary>
    /// A ceiling on upload size. Generous for a chapter or a deck, and low enough that a
    /// mis-drop does not fill the disk.
    /// </summary>
    private const long MaxUploadBytes = 32 * 1024 * 1024;

    public static void MapCourseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/formats", (DocumentExtractors extractors) =>
            Results.Ok(new { supported = extractors.SupportedExtensions }));

        app.MapPost("/api/courses", async (HttpRequest request, IngestionPipeline pipeline) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "Send the document as multipart/form-data." });

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No file arrived. Choose a document and try again." });

            if (file.Length > MaxUploadBytes)
                return Results.BadRequest(new { error = $"That file is larger than the {MaxUploadBytes / (1024 * 1024)}MB limit." });

            try
            {
                await using var content = file.OpenReadStream();
                var document = pipeline.Begin(DefaultTenant, file.FileName, file.ContentType, content);
                return Results.Accepted($"/api/documents/{document.Id}/status", Status(document));
            }
            catch (UnsupportedFormatException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        })
        .DisableAntiforgery();

        app.MapGet("/api/documents/{documentId:guid}/status", (Guid documentId, IngestionPipeline pipeline) =>
        {
            var document = pipeline.Status(documentId);
            return document is null ? Results.NotFound() : Results.Ok(Status(document));
        });

        app.MapGet("/api/courses", (ICourseStore store) => Results.Ok(store.List().Select(course => new
        {
            id = course.Id,
            title = course.Title,
            createdAt = course.CreatedAt,
        })));

        app.MapGet("/api/courses/{courseId:guid}", (Guid courseId, ICourseStore store) =>
        {
            var composed = store.Find(courseId);
            if (composed is null) return Results.NotFound();

            return Results.Ok(new
            {
                id = composed.Course.Id,
                title = composed.Course.Title,
                lessons = composed.Lessons.OrderBy(lesson => lesson.Ordinal).Select(lesson => new
                {
                    id = lesson.Id,
                    title = lesson.Title,
                    ordinal = lesson.Ordinal,
                    nodeCount = composed.ScriptFor(lesson.Id).Count,
                }),
            });
        });
    }

    private static object Status(SourceDocument document) => new
    {
        documentId = document.Id,
        courseId = document.CourseId,
        stage = document.Stage,
        percent = IngestionStages.PercentComplete(document.Stage),
        message = document.Failure ?? IngestionStages.Describe(document.Stage),
        ready = document.IsReady,
        failed = document.HasFailed,
        words = document.ExtractedWordCount,
    };
}
