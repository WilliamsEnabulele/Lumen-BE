using Lumen.Domain.Ingestion;

namespace Lumen.Domain.Teaching;

/// <summary>
/// What the AI decides a document teaches.
///
/// A plan, deliberately — not a script. It says what each beat is *for*, what the student
/// should be able to do afterwards, and what is worth drawing; it does not say the words. The
/// words are generated live while teaching, because a tutor reading prose it wrote earlier
/// cannot react to the student in front of it, and reacting is the entire product.
///
/// The plan is also the unit an instructor reviews. Structure is where a generation error does
/// lasting damage — a wrong prerequisite mis-teaches everyone who hits it — and it is bounded
/// work to check, which a transcript of every sentence is not.
/// </summary>
public sealed record LessonPlan(string CourseTitle, string Summary, IReadOnlyList<PlannedLesson> Lessons)
{
    public static LessonPlan Empty(string title) => new(title, string.Empty, []);

    public int ConceptCount => Lessons.Sum(lesson => lesson.Concepts.Count);
}

public sealed record PlannedLesson(
    string Title,
    /// <summary>What the student should be able to do at the end. Drives the comprehension checks.</summary>
    string Objective,
    IReadOnlyList<PlannedConcept> Concepts);

public sealed record PlannedConcept(
    string Title,
    /// <summary>What to get across, in a sentence. The tutor phrases it live; this is the brief.</summary>
    string TeachingIntent,
    /// <summary>Titles of concepts that must come first. Validated into a graph before publishing.</summary>
    IReadOnlyList<string> Prerequisites,
    /// <summary>The passage this came from, so a generated claim can be traced and withdrawn.</summary>
    string SourceRef,
    /// <summary>Verbatim source the tutor is grounded in while teaching this concept.</summary>
    string SourceExcerpt,
    /// <summary>A hint at what would be worth drawing. The tutor decides in the moment.</summary>
    string? VisualHint);

/// <summary>
/// Reads a document and decides what it teaches.
///
/// The interface exists because the answer has to be replaceable: this is the most expensive
/// and most judgement-heavy call in the system, and the deterministic fallback behind it is
/// what keeps the product runnable when there is no key and honest when there is no model.
/// </summary>
public interface ILessonAuthor
{
    /// <summary>A name for what produced this plan, recorded with the course.</summary>
    string Name { get; }

    Task<LessonPlan> AuthorAsync(ExtractedDocument document, CancellationToken cancellationToken = default);
}
