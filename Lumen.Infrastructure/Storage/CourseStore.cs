using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumen.Domain.Courses;
using Lumen.Domain.Ingestion;

namespace Lumen.Infrastructure.Storage;

public interface ICourseStore
{
    void Save(ComposedCourse composed);
    ComposedCourse? Find(Guid courseId);
    IReadOnlyList<Course> List();
}

/// <summary>
/// Composed courses, written as JSON next to the uploads.
///
/// This is the same bargain Registraa struck with local disk storage: a flow nobody can run is
/// a flow nobody checks, and requiring a database before anyone can see a document become a
/// lesson would make the interesting part of this system the part nobody exercises.
///
/// It is explicitly the first implementation, not the last. Relational storage arrives with
/// the EF model and its migrations — sessions, mastery and assessment attempts all want rows
/// and indexes, and none of them want to be a JSON blob.
/// </summary>
public sealed class FileCourseStore : ICourseStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ConcurrentDictionary<Guid, ComposedCourse> _courses = new();
    private readonly string _root;

    public FileCourseStore(string root)
    {
        _root = Path.Combine(root, "courses");
        Directory.CreateDirectory(_root);
        Rehydrate();
    }

    public void Save(ComposedCourse composed)
    {
        ArgumentNullException.ThrowIfNull(composed);
        _courses[composed.Course.Id] = composed;

        // Written to a temporary file and moved into place: a process that dies mid-write
        // leaves the previous course intact rather than a half-parsed one.
        var path = Path.Combine(_root, $"{composed.Course.Id}.json");
        var staging = path + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(composed, Json));
        File.Move(staging, path, overwrite: true);
    }

    public ComposedCourse? Find(Guid courseId) =>
        _courses.TryGetValue(courseId, out var composed) ? composed : null;

    public IReadOnlyList<Course> List() =>
        _courses.Values.Select(composed => composed.Course)
            .OrderByDescending(course => course.CreatedAt)
            .ToArray();

    private void Rehydrate()
    {
        foreach (var path in Directory.EnumerateFiles(_root, "*.json"))
        {
            try
            {
                var composed = JsonSerializer.Deserialize<ComposedCourse>(File.ReadAllText(path), Json);
                if (composed is not null) _courses[composed.Course.Id] = composed;
            }
            catch (JsonException)
            {
                // A course we cannot read is not a reason to refuse to start. It will be
                // reported as missing, which is the truth, and can be re-ingested.
            }
        }
    }
}

public interface IUploadStorage
{
    /// <summary>Returns the opaque key the bytes were written under.</summary>
    string Save(Guid courseId, string fileName, Stream content);

    Stream Open(string objectKey);
}

/// <summary>
/// Uploads on local disk.
///
/// Keys are opaque and carry no file name, because keys reach logs, metrics and error reports,
/// and a key reading "adaeze-okonkwo-thesis.docx" leaks everywhere the key travels.
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
