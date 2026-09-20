using System.Security.Cryptography;
using System.Text;
using Lumen.Domain.Common;

namespace Lumen.Domain.Accounts;

/// <summary>
/// A signed-in session.
///
/// An opaque token kept server-side rather than a signed one the client carries, because the
/// question this has to answer well is "has this been revoked" — and a self-contained token
/// answers it with "not until it expires". Signing out, a stolen laptop and a compromised
/// account all need a session to stop working now.
/// </summary>
public sealed class AuthSession : Entity
{
    public Guid StudentId { get; set; }

    /// <summary>
    /// The SHA-256 of the token, never the token.
    ///
    /// Same reasoning as the password beside it: whoever reads this store must not come away
    /// able to sign in as anybody. The token itself exists once, in the reply that issued it,
    /// and after that only the holder has it.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsUsableAt(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>
    /// How long a sign-in lasts. Long enough that a student is not asked again mid-lesson,
    /// short enough that a session left open on a shared machine does not last a term.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>A fresh token, and the session that will recognise it. The token is returned once.</summary>
    public static (AuthSession Session, string Token) Issue(Guid studentId, DateTimeOffset now)
    {
        // 256 bits from a cryptographic source. Base64url so it survives a cookie, a header and
        // a URL without anything re-encoding it on the way.
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));

        var session = new AuthSession
        {
            StudentId = studentId,
            TokenHash = HashOf(token),
            ExpiresAt = now + Lifetime,
            CreatedAt = now,
            UpdatedAt = now,
        };

        return (session, token);
    }

    public static string HashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
