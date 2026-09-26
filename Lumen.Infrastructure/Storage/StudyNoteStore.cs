using System.Collections.Concurrent;
using Lumen.Domain.Study;

namespace Lumen.Infrastructure.Storage;

/// <summary>
/// What a student kept from a course.
///
/// Every read is scoped to an owner, and there is deliberately no way to fetch a note by id
/// alone. Notes are the most personal thing this server holds — they are what somebody thought
/// while they were confused — and an accessor that merely finds one is an accessor a handler
/// can forget to check.
/// </summary>
public interface IStudyNoteStore
{
    void Save(StudyNote note);

    /// <summary>This student's notes on this course, most recently kept first.</summary>
    IReadOnlyList<StudyNote> ListFor(Guid studentId, Guid courseId);

    /// <summary>
    /// The note, only if it is this student's. Somebody else's is null rather than theirs,
    /// which the endpoint turns into a 404 rather than a 403 — "forbidden" confirms the id is
    /// real, which is the one thing a stranger guessing ids wants to learn.
    /// </summary>
    StudyNote? FindFor(Guid studentId, Guid noteId);

    void Delete(StudyNote note);
}

public sealed class InMemoryStudyNoteStore : IStudyNoteStore
{
    private readonly ConcurrentDictionary<Guid, StudyNote> _notes = new();

    public void Save(StudyNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        _notes[note.Id] = note;
    }

    public IReadOnlyList<StudyNote> ListFor(Guid studentId, Guid courseId) => Ordered(_notes.Values, studentId, courseId);

    public StudyNote? FindFor(Guid studentId, Guid noteId) =>
        _notes.TryGetValue(noteId, out var note) && note.StudentId == studentId ? note : null;

    public void Delete(StudyNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        _notes.TryRemove(note.Id, out _);
    }

    internal static StudyNote[] Ordered(IEnumerable<StudyNote> notes, Guid studentId, Guid courseId) =>
        notes
            .Where(note => note.StudentId == studentId && note.CourseId == courseId)
            // Newest first: the thing just kept is the thing being looked at, and a list that
            // appends to the bottom makes somebody scroll to see what they have done.
            .OrderByDescending(note => note.CreatedAt)
            .ThenByDescending(note => note.Id)
            .ToArray();
}

public sealed class FileStudyNoteStore : IStudyNoteStore
{
    private readonly ConcurrentDictionary<Guid, StudyNote> _notes = new();
    private readonly string _root;

    public FileStudyNoteStore(string root)
    {
        _root = Path.Combine(root, "notes");
        Directory.CreateDirectory(_root);

        foreach (var note in JsonFiles.ReadAll<StudyNote>(_root)) _notes[note.Id] = note;
    }

    public void Save(StudyNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        _notes[note.Id] = note;
        JsonFiles.Write(PathFor(note), note);
    }

    public IReadOnlyList<StudyNote> ListFor(Guid studentId, Guid courseId) =>
        InMemoryStudyNoteStore.Ordered(_notes.Values, studentId, courseId);

    public StudyNote? FindFor(Guid studentId, Guid noteId) =>
        _notes.TryGetValue(noteId, out var note) && note.StudentId == studentId ? note : null;

    public void Delete(StudyNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        _notes.TryRemove(note.Id, out _);

        // Deleted from disk as well as from the index. A note removed in the app and still on
        // the server is a note that comes back on the next restart, which is worse than one
        // that never deleted at all — the student watched it go.
        var path = PathFor(note);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(StudyNote note) => Path.Combine(_root, $"{note.Id}.json");
}
