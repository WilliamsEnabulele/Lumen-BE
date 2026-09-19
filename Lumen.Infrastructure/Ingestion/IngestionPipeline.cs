using System.Collections.Concurrent;
using Lumen.Domain.Ingestion;
using Lumen.Domain.Teaching;
using Lumen.Infrastructure.Extraction;
using Lumen.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Ingestion;

/// <summary>
/// Upload to teachable, one stage at a time.
///
/// The stages are visible to the student because the wait is real — a model reading a document
/// and working out what it teaches takes as long as it takes, and a progress bar that invents a
/// percentage is a lie a student catches.
///
/// Work runs on a background task here. In production it belongs on a durable queue, so a
/// process restart resumes an ingestion instead of losing it; the stage on
/// <see cref="SourceDocument"/> is already the resume point that needs.
/// </summary>
public sealed class IngestionPipeline(
    DocumentExtractors extractors,
    ILessonAuthor author,
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
        _ = Task.Run(() => RunAsync(document));
        return document;
    }

    public SourceDocument? Status(Guid documentId) =>
        _documents.TryGetValue(documentId, out var document) ? document : null;

    private async Task RunAsync(SourceDocument document)
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
            var plan = await author.AuthorAsync(extracted);

            if (plan.ConceptCount == 0)
            {
                Fail(document, "There was not enough in that document to teach from.");
                return;
            }

            document.Stage = IngestionStage.WritingScript;

            // A plan whose prerequisites form a cycle cannot be taught in any order, and would
            // mis-teach everyone who took it. Better to refuse the upload than to publish one.
            if (LessonPlanValidator.CycleIn(plan) is { } cycle)
            {
                Fail(document, $"The lesson plan doubles back on itself around “{cycle}”. Try uploading again.");
                logger.LogWarning("Authored plan for {CourseId} contained a prerequisite cycle.", document.CourseId);
                return;
            }

            courses.Save(new StoredCourse(document.CourseId, plan, author.Name, DateTimeOffset.UtcNow));
            document.Stage = IngestionStage.Ready;

            logger.LogInformation(
                "{Author} turned {Words} words into {Lessons} lessons and {Concepts} concepts for course {CourseId}.",
                author.Name, extracted.WordCount, plan.Lessons.Count, plan.ConceptCount, document.CourseId);
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
