using Lumen.Domain.Accounts;
using Lumen.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;

namespace Lumen.Infrastructure.Accounts;

/// <summary>Who is making this request, resolved once per request from the session cookie.</summary>
public interface ISignedIn
{
    /// <summary>The student, or null when nobody is signed in.</summary>
    Student? Student { get; }

    bool IsSignedIn { get; }

    /// <summary>
    /// The student's id, or throws.
    ///
    /// For handlers that have already refused anonymous callers. Throwing beats returning
    /// <c>Guid.Empty</c>, which would quietly file somebody's course under an owner nobody has
    /// — and then hand it to the next anonymous caller.
    /// </summary>
    Guid Id { get; }
}

/// <inheritdoc />
public sealed class SignedIn(IHttpContextAccessor accessor, IStudentStore students, IAuthSessionStore sessions)
    : ISignedIn
{
    /// <summary>
    /// The cookie the session travels in. HttpOnly, so a script that gets onto the page cannot
    /// read it — which is the single largest difference between this and keeping a token in
    /// local storage.
    /// </summary>
    public const string Cookie = "lumen_session";

    private Student? _student;
    private bool _resolved;

    public Student? Student
    {
        get
        {
            if (_resolved) return _student;
            _resolved = true;

            var token = accessor.HttpContext?.Request.Cookies[Cookie];
            if (string.IsNullOrWhiteSpace(token)) return null;

            var session = sessions.FindByTokenHash(AuthSession.HashOf(token));
            if (session is null || !session.IsUsableAt(DateTimeOffset.UtcNow)) return null;

            _student = students.Find(session.StudentId);
            return _student;
        }
    }

    public bool IsSignedIn => Student is not null;

    public Guid Id =>
        Student?.Id ?? throw new InvalidOperationException("This handler requires a signed-in student.");
}
