using Lumen.Domain.Common;

namespace Lumen.Domain.Billing;

/// <summary>
/// One document turned into a course, recorded so the free allowance can be counted.
///
/// A row per upload rather than a counter per student, because a counter has to be reset by
/// something, and whatever resets it is a scheduled job that will one day not run — leaving
/// either a student locked out or an allowance that never ends. Counting rows in a month needs
/// nothing to run at all.
/// </summary>
public sealed class UploadRecord : Entity
{
    public Guid StudentId { get; set; }
    public DateTimeOffset At { get; set; }

    /// <summary>The document this was, so the count can be explained rather than just asserted.</summary>
    public Guid DocumentId { get; set; }

    public bool IsInMonthOf(DateTimeOffset when) =>
        At.UtcDateTime.Year == when.UtcDateTime.Year && At.UtcDateTime.Month == when.UtcDateTime.Month;
}
