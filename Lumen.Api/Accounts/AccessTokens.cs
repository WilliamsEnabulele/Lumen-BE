using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Lumen.Api.Accounts;

/// <summary>
/// How access tokens are signed and checked.
///
/// The signing key is the whole of the security here: anybody holding it can mint a token for
/// any student. So there is no default — a server outside development refuses to start without
/// one rather than falling back to a value that would be identical on every deployment and
/// published in this repository.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Auth:Jwt";

    public string Issuer { get; set; } = "lumen";
    public string Audience { get; set; } = "lumen-app";

    /// <summary>At least 32 bytes, because HMAC-SHA256 with a shorter key is a shorter key.</summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// How long an access token is good for.
    ///
    /// Short, and that is the entire bargain. A signed token cannot be withdrawn, so the only
    /// thing limiting the damage of a stolen one is how quickly it stops working. Fifteen
    /// minutes is the window somebody signed out of a stolen laptop is still exposed for.
    /// </summary>
    public TimeSpan AccessLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SigningKey) && SigningKey.Length >= 32;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey!));
}

/// <summary>
/// Mints the short-lived token the API is read with.
///
/// Claims are kept to what authorisation actually needs — who this is, which session it came
/// from, and when it stops. A name or an email in here would be a copy of the account that
/// goes stale the moment somebody edits theirs, and one that travels in every request.
/// </summary>
public sealed class AccessTokens(JwtOptions options)
{
    /// <summary>The session this token was minted from, so revoking that session is traceable.</summary>
    public const string SessionClaim = "sid";

    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid studentId, Guid sessionId, DateTimeOffset now)
    {
        var expires = now + options.AccessLifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, studentId.ToString()),
                new Claim(SessionClaim, sessionId.ToString()),
                // A unique id per token, so one can be named in a log without naming the token.
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            ]),
            SigningCredentials = new SigningCredentials(options.Key(), SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
