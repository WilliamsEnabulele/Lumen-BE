using Lumen.Domain.Common;

namespace Lumen.Domain.Courses;

/// <summary>
/// The unit a student is measured against. Mastery records, assessment items and adaptive
/// decisions all point here, which is why <see cref="Key"/> has to survive reprocessing.
/// </summary>
public sealed class Concept : Entity
{
    public Guid CourseId { get; set; }
    public Guid LessonId { get; set; }

    /// <summary>Stable across ingestion runs. See <see cref="ConceptKey"/>.</summary>
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>Changes when the content changes, so reprocessing knows what to rebuild.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Keys of concepts that must be taught before this one.</summary>
    public List<string> Prerequisites { get; set; } = [];

    /// <summary>
    /// Where this came from in the source document. Generated teaching content has to be
    /// traceable back to its source for accuracy review and correction — a claim nobody can
    /// locate is a claim nobody can withdraw.
    /// </summary>
    public string? SourceRef { get; set; }
}
