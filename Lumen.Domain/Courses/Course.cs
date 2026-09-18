using Lumen.Domain.Common;

namespace Lumen.Domain.Courses;

public sealed class Course : Entity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Locale { get; set; } = "en-NG";
    public CoursePublicationState State { get; set; } = CoursePublicationState.Draft;
}

/// <summary>
/// Nothing reaches a student unreviewed. The reviewer's unit of work is the lesson graph —
/// concepts, ordering, prerequisite edges — not the prose, because reviewing generated prose
/// line by line is unbounded work and a wrong prerequisite edge does far more damage than an
/// awkward sentence.
/// </summary>
public enum CoursePublicationState
{
    Draft = 0,
    AwaitingReview = 1,
    Published = 2,
    Withdrawn = 3
}

public sealed class Module : Entity
{
    public Guid CourseId { get; set; }
    public int Ordinal { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class Lesson : Entity
{
    public Guid CourseId { get; set; }
    public Guid ModuleId { get; set; }
    public int Ordinal { get; set; }
    public string Title { get; set; } = string.Empty;
}
