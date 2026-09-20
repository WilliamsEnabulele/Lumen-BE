using System.Collections.Concurrent;
using Lumen.Domain.Accounts;

namespace Lumen.Infrastructure.Storage;

public interface IStudentStore
{
    void Save(Student student);
    Student? Find(Guid studentId);

    /// <summary>By address, normalised. The lookup sign-in is built on.</summary>
    Student? FindByEmail(string email);
}

public interface IAuthSessionStore
{
    void Save(AuthSession session);

    /// <summary>By the hash of the token presented, because the token itself is never stored.</summary>
    AuthSession? FindByTokenHash(string tokenHash);

    /// <summary>Ends every session a student has. What "sign out everywhere" is built on.</summary>
    void RevokeAllFor(Guid studentId, DateTimeOffset now);
}

public sealed class FileStudentStore : IStudentStore
{
    private readonly ConcurrentDictionary<Guid, Student> _byId = new();
    private readonly ConcurrentDictionary<string, Guid> _byEmail = new(StringComparer.Ordinal);
    private readonly string _root;

    public FileStudentStore(string root)
    {
        _root = Path.Combine(root, "students");
        Directory.CreateDirectory(_root);

        foreach (var student in JsonFiles.ReadAll<Student>(_root)) Index(student);
    }

    public void Save(Student student)
    {
        ArgumentNullException.ThrowIfNull(student);

        Index(student);
        JsonFiles.Write(Path.Combine(_root, $"{student.Id}.json"), student);
    }

    public Student? Find(Guid studentId) => _byId.TryGetValue(studentId, out var student) ? student : null;

    public Student? FindByEmail(string email) =>
        _byEmail.TryGetValue(Student.KeyFor(email), out var id) ? Find(id) : null;

    private void Index(Student student)
    {
        _byId[student.Id] = student;
        _byEmail[student.EmailKey] = student.Id;
    }
}

/// <summary>
/// Sessions on disk.
///
/// Persisted rather than held in memory, because a restart that signs everybody out is a
/// restart nobody wants to do — which is how a server ends up not being restarted when it
/// should be.
/// </summary>
public sealed class FileAuthSessionStore : IAuthSessionStore
{
    private readonly ConcurrentDictionary<string, AuthSession> _byTokenHash = new(StringComparer.Ordinal);
    private readonly string _root;

    public FileAuthSessionStore(string root)
    {
        _root = Path.Combine(root, "auth-sessions");
        Directory.CreateDirectory(_root);

        foreach (var session in JsonFiles.ReadAll<AuthSession>(_root)) _byTokenHash[session.TokenHash] = session;
    }

    public void Save(AuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _byTokenHash[session.TokenHash] = session;
        JsonFiles.Write(Path.Combine(_root, $"{session.Id}.json"), session);
    }

    public AuthSession? FindByTokenHash(string tokenHash) =>
        _byTokenHash.TryGetValue(tokenHash, out var session) ? session : null;

    public void RevokeAllFor(Guid studentId, DateTimeOffset now)
    {
        foreach (var session in _byTokenHash.Values.Where(session => session.StudentId == studentId))
        {
            if (session.RevokedAt is not null) continue;

            session.RevokedAt = now;
            session.UpdatedAt = now;
            Save(session);
        }
    }
}
