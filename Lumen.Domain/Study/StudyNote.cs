using Lumen.Domain.Common;

namespace Lumen.Domain.Study;

/// <summary>Which of the two things a student kept, which are not the same thing at all.</summary>
public enum NoteKind
{
    /// <summary>
    /// A line the tutor said, kept because it landed. The words are the tutor's, so it carries
    /// a <see cref="StudyNote.SourceRef"/> back to the material they came from.
    /// </summary>
    KeyPoint,

    /// <summary>Something the student wrote themselves. Their words, and nobody else's to edit.</summary>
    Note,
}

/// <summary>Why keeping something was refused, in a form the endpoint can turn into words.</summary>
public enum NoteRefusal
{
    None = 0,
    Empty,
    TooLong,
    AlreadyKept,
}

/// <summary>
/// Something a student kept from a lesson.
///
/// Hung off the course rather than the teaching session on purpose. A session is one sitting;
/// coming back tomorrow starts another, and notes that vanished with the last one would be
/// notes nobody would trust enough to write. The session is recorded on it, so a note can still
/// say which sitting it came from, but it is not what owns it.
/// </summary>
public sealed class StudyNote : Entity
{
    public Guid StudentId { get; set; }
    public Guid CourseId { get; set; }
    public NoteKind Kind { get; set; }
    public string Body { get; set; } = string.Empty;

    /// <summary>What was being taught when this was kept, so a list of them reads as a lesson.</summary>
    public string? ConceptTitle { get; set; }

    /// <summary>Which sitting it came from. Null for a note written outside a lesson.</summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Where in the material the tutor got it.
    ///
    /// Carried for a kept line for the same reason generated teaching content carries one: a
    /// claim nobody can locate is a claim nobody can withdraw. Null for a student's own note,
    /// which is not a claim about the material but a claim about what they thought.
    /// </summary>
    public string? SourceRef { get; set; }
}

/// <summary>
/// The rules about keeping things, which are worth testing away from a request.
///
/// The one with teeth is the duplicate rule, and it is deliberately asymmetric. A kept line is
/// the tutor's words, so keeping it twice is always a mistake — a double-press, or the same
/// line saved again on a second pass — and two identical entries in a revision list is noise
/// the student has to clear. A note is the student's own words, and two that happen to read
/// the same are two thoughts they had; refusing the second would be this code telling somebody
/// what they meant.
/// </summary>
public static class StudyNotes
{
    /// <summary>
    /// Long enough for a paragraph of thinking, short enough that one note is not a way to
    /// hand the server a novel. A cap nobody notices, on a store where the record is a file.
    /// </summary>
    public const int MaximumLength = 4000;

    public static NoteRefusal CheckKeep(NoteKind kind, string? body, IEnumerable<StudyNote> alreadyKept)
    {
        ArgumentNullException.ThrowIfNull(alreadyKept);

        var trimmed = Normalise(body);

        if (trimmed.Length == 0) return NoteRefusal.Empty;
        if (trimmed.Length > MaximumLength) return NoteRefusal.TooLong;

        if (kind == NoteKind.KeyPoint && alreadyKept.Any(kept => IsSame(kept, kind, trimmed)))
            return NoteRefusal.AlreadyKept;

        return NoteRefusal.None;
    }

    /// <summary>What the student is told. Written for a person, and never more than they need.</summary>
    public static string Explain(NoteRefusal refusal) => refusal switch
    {
        NoteRefusal.Empty => "There is nothing to keep.",
        NoteRefusal.TooLong => $"That is longer than {MaximumLength} characters. Keep the part that matters.",
        NoteRefusal.AlreadyKept => "That line is already in your key points.",
        _ => "That could not be kept.",
    };

    /// <summary>
    /// The body as it will be stored.
    ///
    /// Trimmed at both ends only. Whitespace inside is the student's — a note laid out in lines
    /// is laid out in lines because somebody laid it out, and tidying that is editing it.
    /// </summary>
    public static string Normalise(string? body) => body?.Trim() ?? string.Empty;

    /// <summary>
    /// Whether two entries are the same thing kept twice.
    ///
    /// Compared case-insensitively and after trimming, because the second press of a button
    /// does not produce a cleaner copy of the same sentence — it produces the same sentence.
    /// </summary>
    public static bool IsSame(StudyNote kept, NoteKind kind, string body)
    {
        ArgumentNullException.ThrowIfNull(kept);

        return kept.Kind == kind
               && string.Equals(kept.Body, Normalise(body), StringComparison.OrdinalIgnoreCase);
    }
}
