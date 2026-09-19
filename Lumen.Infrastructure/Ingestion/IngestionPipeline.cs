using System.Collections.Concurrent;
using Lumen.Domain.Ingestion;
using Lumen.Infrastructure.Extraction;
using Lumen.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Ingestion;

/// <summary>
/// Upload to teachable, one stage at a time.
///
/// The stages are visible to the student because the wait is real — reading a document and
/// working out what it teaches takes as long as it takes, and a progress bar that invents a
/// percentage is a lie a student catches. Each stage is recorded as it is entered, so a run
/// that dies is known to have died in extraction rather than simply stopping.
///
/// Work runs on a background task here. In production it belongs on a durable queue, so a
/// process restart resumes an ingestion instead of losing it — the stage on
/// <see cref="SourceDocument"/> is already the resume point that needs.
/// </summary>
public sealed class IngestionPipeline(
    DocumentExtractors extractors,
    ICourseStore courses,
    IUploadStorage uploads,
    ILogger<IngestionPipeline> logger)
{
    private readonly ConcurrentDictionary<Guid, SourceDocument> _documents = new();

    public SourceDocument Begin(Guid tenantId, string fileName, string contentType, Stream content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extractors.For(fileName) is null)
            throw new UnsupportedFormatException(extension, extractors.SupportedExtensions);

        var courseId = Guid.CreateVersion7();
        var objectKey = uploads.Save(courseId, fileName, content);

        var document = new SourceDocument
        {
            TenantId = tenantId,
            CourseId = courseId,
            FileName = fileName,
            ContentType = contentType,
            ObjectKey = objectKey,
            Stage = IngestionStage.Received,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _documents[document.Id] = document;
        _ = Task.Run(() => Run(tenantId, document));
        return document;
    }

    public SourceDocument? Status(Guid documentId) =>
        _documents.TryGetValue(documentId, out var document) ? document : null;

    public SourceDocument? ForCourse(Guid courseId) =>
        _documents.Values.FirstOrDefault(document => document.CourseId == courseId);

    private void Run(Guid tenantId, SourceDocument document)
    {
        try
        {
            document.Stage = IngestionStage.Extracting;
            var extractor = extractors.For(document.FileName)!;

            ExtractedDocument extracted;
            using (var content = uploads.Open(document.ObjectKey))
            {
                extracted = extractor.Extract(content, document.FileName);
            }

            document.ExtractedWordCount = extracted.WordCount;

            if (extracted.Sections.Count == 0)
            {
                Fail(document, "There was no readable text in that file. If it is a scanned image, it needs to be run through OCR first.");
                return;
            }

            document.Stage = IngestionStage.Structuring;
            var composed = LessonComposer.Compose(tenantId, extracted);

            if (composed.ScriptNodes.Count == 0)
            {
                Fail(document, "There was not enough in that document to teach from.");
                return;
            }

            document.Stage = IngestionStage.WritingScript;
            composed.Course.Id = document.CourseId;
            foreach (var module in composed.Modules) module.CourseId = document.CourseId;
            foreach (var lesson in composed.Lessons) lesson.CourseId = document.CourseId;
            foreach (var concept in composed.Concepts) concept.CourseId = document.CourseId;

            courses.Save(composed);
            document.Stage = IngestionStage.Ready;

            logger.LogInformation(
                "Ingested {Words} words into {Lessons} lessons and {Nodes} script nodes for course {CourseId}.",
                extracted.WordCount, composed.Lessons.Count, composed.ScriptNodes.Count, document.CourseId);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            Fail(document, "That file could not be read. It may be corrupt, or not the format its name says it is.");
            logger.LogWarning(exception, "Ingestion failed reading document {DocumentId}.", document.Id);
        }
        catch (Exception exception)
        {
            Fail(document, "Something went wrong turning that document into a lesson.");
            logger.LogError(exception, "Ingestion failed for document {DocumentId}.", document.Id);
        }
    }

    private static void Fail(SourceDocument document, string reason)
    {
        document.Stage = IngestionStage.Failed;
        document.Failure = reason;
    }
}
