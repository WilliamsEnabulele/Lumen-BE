using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumen.Domain.Teaching;

namespace Lumen.Infrastructure.Storage;

/// <param name="AuthoredBy">
/// Which author produced this plan, recorded with the course. A plan written by a model and one
/// written by the deterministic fallback are not the same artefact, and a reviewer needs to
/// know which they are looking at.
/// </param>
public sealed record StoredCourse(Guid Id, LessonPlan Plan, string AuthoredBy, DateTimeOffset CreatedAt);

public interface ICourseStore
{
    void Save(StoredCourse course);
    StoredCourse? Find(Guid courseId);
    IReadOnlyList<StoredCourse> List();
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
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ConcurrentDictionary<Guid, StoredCourse> _courses = new();
    private readonly string _root;

    public FileCourseStore(string root)
    {
        _root = Path.Combine(root, "courses");
        Directory.CreateDirectory(_root);
        Rehydrate();
    }

    public void Save(StoredCourse course)
    {
        ArgumentNullException.ThrowIfNull(course);
        _courses[course.Id] = course;

        // Written aside and moved into place, so a process that dies mid-write leaves the
        // previous course intact rather than a half-parsed one.
        var path = Path.Combine(_root, $"{course.Id}.json");
        var staging = path + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(course, Json));
        File.Move(staging, path, overwrite: true);
    }

    public StoredCourse? Find(Guid courseId) =>
        _courses.TryGetValue(courseId, out var course) ? course : null;

    public IReadOnlyList<StoredCourse> List() =>
        _courses.Values.OrderByDescending(course => course.CreatedAt).ToArray();

    private void Rehydrate()
    {
        foreach (var path in Directory.EnumerateFiles(_root, "*.json"))
        {
            try
            {
                var course = JsonSerializer.Deserialize<StoredCourse>(File.ReadAllText(path), Json);
                if (course is not null) _courses[course.Id] = course;
            }
            catch (JsonException)
            {
                // A course we cannot read is not a reason to refuse to start. It reports as
                // missing, which is the truth, and can be re-ingested.
            }
        }
    }
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
/// In memory, deliberately, for now: a session is a conversation in progress, and the thing
/// that must survive a restart is the position in the plan, not the transcript. Persisting it
/// is the same work as persisting mastery, and belongs in the same change.
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
