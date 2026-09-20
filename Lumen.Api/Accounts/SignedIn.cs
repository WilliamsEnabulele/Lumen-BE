using System.Security.Claims;
using Lumen.Domain.Accounts;
using Lumen.Infrastructure.Storage;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Lumen.Api.Accounts;

/// <summary>Who is making this request, resolved from the access token it carried.</summary>
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

/// <summary>
/// Reads the signed-in student out of the validated access token.
///
/// The token has already been checked by the JWT middleware — signature, issuer, audience and
/// expiry — before this runs, so this is a claim lookup rather than a second verification.
/// Deliberately no trip to the session store: a token is trusted for the fifteen minutes it
/// lives, and checking every request against a database would be paying a stateless design's
/// price while keeping a stateful one's cost.
///
/// The consequence is the one worth knowing: signing out stops the next refresh, not the
/// current token. That window is the access lifetime, and it is why the access lifetime is
/// short.
/// </summary>
public sealed class SignedIn(IHttpContextAccessor accessor, IStudentStore students) : ISignedIn
{
    /// <summary>
    /// The cookie the refresh token travels in.
    ///
    /// HttpOnly, so a script on the page cannot read it — which matters more here than it did
    /// when this cookie held everything: the refresh token is now the long-lived half, and the
    /// access token beside it is deliberately cheap to lose.
    /// </summary>
    public const string RefreshCookie = "lumen_refresh";

    private Student? _student;
    private bool _resolved;

    public Student? Student
    {
        get
        {
            if (_resolved) return _student;
            _resolved = true;

            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true) return null;

            // ClaimTypes.NameIdentifier is where the handler maps "sub" to by default; the raw
            // name is read too, because that mapping is a setting somebody can turn off.
            var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (!Guid.TryParse(subject, out var studentId)) return null;

            _student = students.Find(studentId);
            return _student;
        }
    }

    public bool IsSignedIn => Student is not null;

    public Guid Id =>
        Student?.Id ?? throw new InvalidOperationException("This handler requires a signed-in student.");
}
