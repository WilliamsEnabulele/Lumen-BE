using System.Collections.Concurrent;
using Lumen.Domain.Assessment;
using Lumen.Domain.Teaching;

namespace Lumen.Infrastructure.Storage;

/// <summary>
/// Teaching sessions on disk.
///
/// The README calls the resume pointer persisted rather than inferred and treats that as
/// non-negotiable, and until now it was held in a dictionary that died with the process. A
/// restart mid-lesson put every student back at the first concept of the first lesson with an
/// empty canvas — which is not a resumption strategy, it is the absence of one.
///
/// The transcript travels with the pointer because the pointer alone does not resume a
/// conversation. A tutor that knows it is on concept four but not that the student already
/// asked twice about loops will teach it again in the same words.
/// </summary>
public sealed class FileSessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<Guid, TeachingSession> _sessions = new();
    private readonly string _root;

    public FileSessionStore(string root)
    {
        _root = Path.Combine(root, "sessions");
        Directory.CreateDirectory(_root);

        foreach (var session in JsonFiles.ReadAll<TeachingSession>(_root)) _sessions[session.Id] = session;
    }

    public void Save(TeachingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.UpdatedAt = DateTimeOffset.UtcNow;
        _sessions[session.Id] = session;
        JsonFiles.Write(Path.Combine(_root, $"{session.Id}.json"), session);
    }

    public TeachingSession? Find(Guid sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;
}

/// <summary>
/// Mastery on disk.
///
/// The part that most had to outlive a restart and least did. A belief is earned over a whole
/// lesson of answered questions, and a student who loses it is taught everything they already
/// demonstrated — so losing this does not merely reset a number, it undoes the adaptation the
/// number exists for.
///
/// Filed under the record's own id rather than its concept key, because the key is derived
/// from a concept title and titles contain slashes, colons and everything else a file name
/// cannot. The index that maps student and concept to a record is rebuilt in memory at startup.
/// </summary>
public sealed class FileMasteryStore : IMasteryStore
{
    private readonly ConcurrentDictionary<(Guid Student, Guid Course, string Concept), MasteryRecord> _records = new();
    private readonly string _root;

    public FileMasteryStore(string root)
    {
        _root = Path.Combine(root, "mastery");
        Directory.CreateDirectory(_root);

        foreach (var record in JsonFiles.ReadAll<MasteryRecord>(_root))
            _records[(record.StudentId, record.CourseId, record.ConceptKey)] = record;
    }

    /// <remarks>
    /// A record created here is not written until something is recorded into it. A file saying
    /// only that a student was once asked about a concept is not worth a write, and it would
    /// come back after a restart as a row in their progress report that means nothing.
    /// </remarks>
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
        JsonFiles.Write(Path.Combine(_root, $"{record.Id}.json"), record);
    }
}
