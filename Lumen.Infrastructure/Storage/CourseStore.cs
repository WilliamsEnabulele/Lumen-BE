using System.Collections.Concurrent;
using Lumen.Domain.Assessment;
using Lumen.Domain.Teaching;

namespace Lumen.Infrastructure.Storage;

/// <param name="AuthoredBy">
/// Which author produced this plan, recorded with the course. A plan written by a model and one
/// written by the deterministic fallback are not the same artefact, and a reviewer needs to
/// know which they are looking at.
/// </param>
/// <param name="OwnerId">
/// Whose document this was. Checked on every read rather than assumed from possession of the
/// id, because a course id travels in URLs and a guessable-or-shared id must not be a way into
/// somebody else's material.
/// </param>
public sealed record StoredCourse(
    Guid Id, LessonPlan Plan, string AuthoredBy, DateTimeOffset CreatedAt, Guid OwnerId)
{
    public bool BelongsTo(Guid studentId) => OwnerId == studentId;
}

public interface ICourseStore
{
    void Save(StoredCourse course);
    StoredCourse? Find(Guid courseId);

    /// <summary>
    /// The course, only if it is this student's. Separate from <see cref="Find"/> so that a
    /// handler which forgets to check ownership has to have been written to forget, rather
    /// than simply not have remembered.
    /// </summary>
    StoredCourse? FindFor(Guid studentId, Guid courseId);

    IReadOnlyList<StoredCourse> ListFor(Guid studentId);
}

/// <summary>
/// Courses on disk as JSON.
///
/// Still the first implementation, not the last: sessions, mastery and assessment attempts all
/// want rows and indexes. A lesson plan, though, is genuinely a document — it is read whole and
/// written whole — so this one may well survive the move to a database.
/// </summary>
public sealed class FileCourseStore : ICourseStore
{
    private readonly ConcurrentDictionary<Guid, StoredCourse> _courses = new();
    private readonly string _root;

    public FileCourseStore(string root)
    {
        _root = Path.Combine(root, "courses");
        Directory.CreateDirectory(_root);

        foreach (var course in JsonFiles.ReadAll<StoredCourse>(_root)) _courses[course.Id] = course;
    }

    public void Save(StoredCourse course)
    {
        ArgumentNullException.ThrowIfNull(course);

        _courses[course.Id] = course;
        JsonFiles.Write(Path.Combine(_root, $"{course.Id}.json"), course);
    }

    public StoredCourse? Find(Guid courseId) =>
        _courses.TryGetValue(courseId, out var course) ? course : null;

    public StoredCourse? FindFor(Guid studentId, Guid courseId) =>
        Find(courseId) is { } course && course.BelongsTo(studentId) ? course : null;

    public IReadOnlyList<StoredCourse> ListFor(Guid studentId) =>
        _courses.Values
            .Where(course => course.BelongsTo(studentId))
            .OrderByDescending(course => course.CreatedAt)
            .ToArray();
}

public interface IUploadStorage
{
    string Save(Guid courseId, string fileName, Stream content);
    Stream Open(string objectKey);
}

/// <summary>
/// Uploads on local disk. Keys are opaque and carry no file name, because keys reach logs,
/// metrics and error reports, and a key reading "adaeze-okonkwo-thesis.docx" leaks everywhere
/// the key travels.
/// </summary>
public sealed class LocalDiskUploadStorage : IUploadStorage
{
    private readonly string _root;

    public LocalDiskUploadStorage(string root)
    {
        _root = Path.Combine(root, "uploads");
        Directory.CreateDirectory(_root);
    }

    public string Save(Guid courseId, string fileName, Stream content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var key = $"{courseId:N}/{Guid.CreateVersion7():N}{extension}";
        var path = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        content.CopyTo(file);

        return key;
    }

    public Stream Open(string objectKey)
    {
        var path = Path.Combine(_root, objectKey.Replace('/', Path.DirectorySeparatorChar));
        return File.OpenRead(path);
    }
}

/// <summary>
/// Live teaching sessions.
///
/// A session is a conversation in progress, and the thing that must survive a restart is the
/// position in the plan — which is why it is a stored object and not something inferred from
/// the transcript afterwards. <see cref="FileSessionStore"/> is what actually stores it;
/// <see cref="InMemorySessionStore"/> is for tests, where a lesson that outlives the test is
/// a leak rather than a feature.
/// </summary>
public interface ISessionStore
{
    void Save(TeachingSession session);
    TeachingSession? Find(Guid sessionId);
}

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<Guid, TeachingSession> _sessions = new();

    public void Save(TeachingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions[session.Id] = session;
    }

    public TeachingSession? Find(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;
}

/// <summary>
/// What each student is believed to know.
///
/// The part that most has to outlive a restart. A belief is earned over a whole lesson of
/// answered questions, and losing it does not merely reset a number — it undoes the adaptation
/// the number exists for, and the student is taught everything they already demonstrated.
/// </summary>
public interface IMasteryStore
{
    MasteryRecord For(Guid studentId, Guid courseId, string conceptKey, string conceptTitle);

    /// <summary>
    /// The record if there is one, without creating it. Separate from <see cref="For"/> because
    /// asking whether a student knows something must not leave behind a record saying they were
    /// asked — a progress report full of untouched concepts is worse than no report.
    /// </summary>
    MasteryRecord? Find(Guid studentId, Guid courseId, string conceptKey);

    IReadOnlyList<MasteryRecord> ForStudent(Guid studentId, Guid courseId);
    void Save(MasteryRecord record);
}

public sealed class InMemoryMasteryStore : IMasteryStore
{
    private readonly ConcurrentDictionary<(Guid Student, Guid Course, string Concept), MasteryRecord> _records = new();

    public MasteryRecord For(Guid studentId, Guid courseId, string conceptKey, string conceptTitle) =>
        _records.GetOrAdd((studentId, courseId, conceptKey), _ => new MasteryRecord
        {
            StudentId = studentId,
            CourseId = courseId,
            ConceptKey = conceptKey,
            ConceptTitle = conceptTitle,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    public MasteryRecord? Find(Guid studentId, Guid courseId, string conceptKey) =>
        _records.TryGetValue((studentId, courseId, conceptKey), out var record) ? record : null;

    public IReadOnlyList<MasteryRecord> ForStudent(Guid studentId, Guid courseId) =>
        _records.Values
            .Where(record => record.StudentId == studentId && record.CourseId == courseId)
            .OrderBy(record => record.ConceptTitle, StringComparer.Ordinal)
            .ToArray();

    public void Save(MasteryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[(record.StudentId, record.CourseId, record.ConceptKey)] = record;
    }
}
