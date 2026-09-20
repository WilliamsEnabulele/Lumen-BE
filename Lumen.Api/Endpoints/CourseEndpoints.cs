using Lumen.Domain.Billing;
using Lumen.Domain.Ingestion;
using Lumen.Infrastructure.Extraction;
using Lumen.Infrastructure.Billing;
using Lumen.Infrastructure.Ingestion;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

public static class CourseEndpoints
{
    /// <summary>
    /// Until there is authentication, everything belongs to one tenant. The column is already on
    /// every row, so switching this for a real claim is a one-line change rather than a migration.
    /// </summary>
    private static readonly Guid DefaultTenant = Guid.Parse("0197b9c2-0000-7000-8000-000000000001");

    /// <summary>Until there is authentication, every upload belongs to the same student.</summary>
    private static readonly Guid DefaultStudent = Guid.Parse("0197b9c2-0000-7000-8000-000000000002");

    /// <summary>Generous for a chapter or a deck, low enough that a mis-drop does not fill the disk.</summary>
    private const long MaxUploadBytes = 32 * 1024 * 1024;

    public static void MapCourseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/formats", (DocumentExtractors extractors) =>
            Results.Ok(new { supported = extractors.SupportedExtensions }));

        app.MapPost("/api/courses", async (
            HttpRequest request,
            IngestionPipeline pipeline,
            IEntitlementStore entitlements,
            IUploadLedger uploads,
            BillingEnforcement billing) =>
        {
            // The metered act. Checked before a byte is read, so somebody out of allowance is
            // told before they wait on an upload that was never going to be accepted.
            var now = DateTimeOffset.UtcNow;
            var used = uploads.CountInMonth(DefaultStudent, now);

            if (!Access.MayUpload(billing.Enforced, entitlements.For(DefaultStudent), used, now))
            {
                return Results.Json(new
                {
                    error = $"That is this month's free document used. A subscription lifts the limit, "
                            + "or the free one comes back next month.",
                    freeAllowanceResetsAt = Access.AllowanceResetsAt(now),
                    plans = "/api/plans",
                }, statusCode: 402);
            }

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

                // Recorded only once the document is genuinely accepted. Counting an upload
                // that was refused for its format would spend somebody's free month on an
                // error message.
                uploads.Record(new UploadRecord
                {
                    StudentId = DefaultStudent,
                    DocumentId = document.Id,
                    At = now,
                    CreatedAt = now,
                });

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
            title = course.Plan.CourseTitle,
            summary = course.Plan.Summary,
            authoredBy = course.AuthoredBy,
            createdAt = course.CreatedAt,
        })));

        app.MapGet("/api/courses/{courseId:guid}", (Guid courseId, ICourseStore store) =>
        {
            var course = store.Find(courseId);
            if (course is null) return Results.NotFound();

            return Results.Ok(new
            {
                id = course.Id,
                title = course.Plan.CourseTitle,
                summary = course.Plan.Summary,
                authoredBy = course.AuthoredBy,
                lessons = course.Plan.Lessons.Select(lesson => new
                {
                    title = lesson.Title,
                    objective = lesson.Objective,
                    concepts = lesson.Concepts.Select(concept => new
                    {
                        title = concept.Title,
                        intent = concept.TeachingIntent,
                        prerequisites = concept.Prerequisites,
                        sourceRef = concept.SourceRef,
                    }),
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
