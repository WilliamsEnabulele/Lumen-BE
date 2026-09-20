using Lumen.Domain.Common;

namespace Lumen.Domain.Ingestion;

/// <summary>What a format-specific extractor produces. Deliberately plain: the intelligence
/// is in composing this into a lesson, not in reading the file.</summary>
public sealed record ExtractedDocument(string Title, IReadOnlyList<ExtractedSection> Sections)
{
    public static ExtractedDocument Empty(string title) => new(title, []);

    public int WordCount => Sections.Sum(section => section.WordCount);
}

public sealed record ExtractedSection(string Heading, int Level, IReadOnlyList<ExtractedBlock> Blocks)
{
    public int WordCount => Blocks.Sum(block => block.WordCount);
}

public enum BlockKind
{
    Paragraph,
    Code,
    ListItem,
    Table,
    Caption
}

/// <param name="SourceRef">
/// Where this came from in the original file — a page, a slide, a paragraph index. It rides
/// all the way through to the script node the student hears, because a generated claim nobody
/// can locate is a claim nobody can withdraw.
/// </param>
public sealed record ExtractedBlock(BlockKind Kind, string Text, string SourceRef)
{
    public int WordCount => Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>An upload, and how far through the pipeline it has got.</summary>
public sealed class SourceDocument : Entity
{
    public Guid CourseId { get; set; }

    /// <summary>Whose upload this is. Carried onto the course it becomes.</summary>
    public Guid OwnerId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Opaque and owner-scoped. Keys reach logs and error reports, so they carry no file name.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    public IngestionStage Stage { get; set; } = IngestionStage.Received;
    public string? Failure { get; set; }

    /// <summary>
    /// Set once extraction has run, so a retry after a crash resumes rather than re-reading the
    /// file. The pipeline is restartable at each stage; nothing is silently lost on a failure.
    /// </summary>
    public int? ExtractedWordCount { get; set; }

    public bool IsReady => Stage == IngestionStage.Ready;
    public bool HasFailed => Stage == IngestionStage.Failed;
}

public enum IngestionStage
{
    Received = 0,
    Extracting = 1,
    Structuring = 2,
    WritingScript = 3,
    Ready = 4,
    Failed = 5
}

public static class IngestionStages
{
    /// <summary>What the student is told while they wait. Stages, not a fake percentage.</summary>
    public static string Describe(IngestionStage stage) => stage switch
    {
        IngestionStage.Received => "Received your document",
        IngestionStage.Extracting => "Reading the document",
        IngestionStage.Structuring => "Working out what it teaches",
        IngestionStage.WritingScript => "Writing the lesson",
        IngestionStage.Ready => "Ready to teach",
        IngestionStage.Failed => "Could not turn this into a lesson",
        _ => "Working"
    };

    public static int PercentComplete(IngestionStage stage) => stage switch
    {
        IngestionStage.Received => 5,
        IngestionStage.Extracting => 30,
        IngestionStage.Structuring => 60,
        IngestionStage.WritingScript => 85,
        IngestionStage.Ready => 100,
        _ => 0
    };
}
