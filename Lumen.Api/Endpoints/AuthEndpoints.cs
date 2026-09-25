using Lumen.Domain.Accounts;
using Lumen.Api.Accounts;
using Lumen.Api.Explorer;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

public sealed record SignUpRequest(string? Email, string? Password, string? Name);

public sealed record SignInRequest(string? Email, string? Password);

/// <summary>
/// Accounts.
///
/// Two tokens, doing different jobs. The <b>access token</b> is a short-lived JWT the client
/// sends on every request; it is verified by signature alone, so reading the API costs no
/// database lookup. The <b>refresh token</b> is opaque, server-side and revocable, lives in an
/// HttpOnly cookie, and is the only thing that can mint a new access token.
///
/// That split is what makes signing out mean something. A signed token cannot be withdrawn, so
/// if it were the only token, "sign out" would mean "stop working in thirty days". Here it
/// means the next refresh fails, and the access token in flight expires within fifteen minutes.
///
/// A sign-in failure says the same thing whichever half was wrong, because distinguishing them
/// hands an attacker a list of real accounts, and hands anybody else a way to find out where a
/// person has signed up.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app, CookiePolicy cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);

        app.MapPost("/api/auth/register", (
            SignUpRequest request,
            IStudentStore students,
            IAuthSessionStore sessions,
            AccessTokens access,
            HttpResponse response) =>
        {
            var email = request.Email?.Trim() ?? string.Empty;

            var refusal = Registration.CheckSignUp(
                email, request.Password, alreadyTaken: students.FindByEmail(email) is not null);

            if (refusal != AccountRefusal.None)
                return Results.BadRequest(new { error = Registration.Explain(refusal) });

            var now = DateTimeOffset.UtcNow;
            var student = new Student
            {
                Email = email,
                EmailKey = Student.KeyFor(email),
                Name = request.Name?.Trim() ?? string.Empty,
                PasswordHash = PasswordHash.Of(request.Password!),
                CreatedAt = now,
                UpdatedAt = now,
                LastSignedInAt = now,
            };
            students.Save(student);

            return SignIn(student, sessions, access, response, cookies, now);
        })
        .RequireRateLimiting(RateLimits.Auth)
        .AllowAnonymous()
        .WithTags(ApiTags.Accounts);

        app.MapPost("/api/auth/login", (
            SignInRequest request,
            IStudentStore students,
            IAuthSessionStore sessions,
            AccessTokens access,
            HttpResponse response) =>
        {
            var student = students.FindByEmail(request.Email ?? string.Empty);

            // Verified even when there is no such account, so the time this takes does not
            // announce which addresses are registered.
            var stored = student?.PasswordHash ?? PasswordHash.Of("a password nobody has");
            var matches = PasswordHash.Matches(request.Password ?? string.Empty, stored);

            if (student is null || !matches)
            {
                return Results.Json(
                    new { error = Registration.Explain(AccountRefusal.WrongEmailOrPassword) },
                    statusCode: 401);
            }

            var now = DateTimeOffset.UtcNow;

            // A correct password checked against an older cost is re-hashed on the way past.
            // That is how a raised iteration count ever reaches somebody who never changes it.
            if (PasswordHash.NeedsRehash(student.PasswordHash))
                student.PasswordHash = PasswordHash.Of(request.Password!);

            student.LastSignedInAt = now;
            student.UpdatedAt = now;
            students.Save(student);

            return SignIn(student, sessions, access, response, cookies, now);
        })
        .RequireRateLimiting(RateLimits.Auth)
        .AllowAnonymous()
        .WithTags(ApiTags.Accounts);

        // Trading the refresh cookie for a new access token. The one place a session is checked
        // against the store, which is what makes revoking it mean anything.
        app.MapPost("/api/auth/refresh", (
            HttpRequest httpRequest,
            IStudentStore students,
            IAuthSessionStore sessions,
            AccessTokens access,
            HttpResponse response) =>
        {
            var presented = httpRequest.Cookies[SignedIn.RefreshCookie];
            if (string.IsNullOrWhiteSpace(presented))
                return Results.Json(new { error = "Nobody is signed in." }, statusCode: 401);

            var now = DateTimeOffset.UtcNow;
            var session = sessions.FindByTokenHash(AuthSession.HashOf(presented));

            if (session is null || !session.IsUsableAt(now) || students.Find(session.StudentId) is not { } student)
            {
                // A cookie that no longer opens anything is cleared rather than left to fail
                // again on every load.
                response.Cookies.Delete(SignedIn.RefreshCookie, cookies.Deletion());
                return Results.Json(new { error = "That session has ended. Sign in again." }, statusCode: 401);
            }

            var (token, expires) = access.Issue(student.Id, session.Id, now);

            return Results.Ok(new
            {
                accessToken = token,
                expiresAt = expires,
                id = student.Id,
                email = student.Email,
                name = student.Name,
            });
        })
        .AllowAnonymous()
        .WithTags(ApiTags.Accounts);

        app.MapPost("/api/auth/logout", (
            HttpRequest httpRequest, IAuthSessionStore sessions, HttpResponse response) =>
        {
            // Revoked server-side as well as cleared client-side. Deleting the cookie alone
            // leaves a working refresh token with anybody who copied it.
            //
            // This session only, not every session the student has: signing out on a laptop
            // should not sign somebody out of their phone, and "everywhere" is a different
            // thing somebody asks for deliberately.
            if (httpRequest.Cookies[SignedIn.RefreshCookie] is { Length: > 0 } presented
                && sessions.FindByTokenHash(AuthSession.HashOf(presented)) is { } session)
            {
                session.RevokedAt = DateTimeOffset.UtcNow;
                session.UpdatedAt = session.RevokedAt.Value;
                sessions.Save(session);
            }

            response.Cookies.Delete(SignedIn.RefreshCookie, cookies.Deletion());

            // The access token already issued keeps working until it expires. Nothing can call
            // it back, which is the cost of it being verifiable without a database.
            return Results.Ok(new { signedOut = true, accessTokenValidFor = "up to 15 minutes" });
        })
        .AllowAnonymous()
        .WithTags(ApiTags.Accounts);

        app.MapGet("/api/auth/me", (ISignedIn signedIn) =>
            signedIn.Student is { } student
                ? Results.Ok(new { id = student.Id, email = student.Email, name = student.Name })
                : Results.Json(new { error = "Nobody is signed in." }, statusCode: 401))
        .WithTags(ApiTags.Accounts);
    }

    private static IResult SignIn(
        Student student,
        IAuthSessionStore sessions,
        AccessTokens access,
        HttpResponse response,
        CookiePolicy cookies,
        DateTimeOffset now)
    {
        var (session, refresh) = AuthSession.Issue(student.Id, now);
        sessions.Save(session);

        // The refresh token goes in the cookie and nowhere else — returning it in the body too
        // would put the long-lived half somewhere a script can read, which is the whole point
        // of HttpOnly. The access token does go in the body, because the client has to be able
        // to put it in a header, and it is the half designed to be cheap to lose.
        response.Cookies.Append(SignedIn.RefreshCookie, refresh, cookies.For(session.ExpiresAt));

        var (token, expires) = access.Issue(student.Id, session.Id, now);

        return Results.Ok(new
        {
            accessToken = token,
            expiresAt = expires,
            id = student.Id,
            email = student.Email,
            name = student.Name,
        });
    }
}

/// <summary>Rate limiter names, in one place so a policy cannot be required under a typo.</summary>
public static class RateLimits
{
    /// <summary>
    /// Guarding sign-in and sign-up.
    ///
    /// A password check is deliberately slow, which protects a stolen database but does nothing
    /// about somebody trying ten thousand passwords against one live account. This does.
    /// </summary>
    public const string Auth = "auth";
}

/// <summary>
/// How the session cookie is written.
///
/// Held as an object built once at startup rather than options repeated at each call site,
/// because the difference between a cookie that is safe and one that is not is four flags, and
/// four flags repeated in three places is four flags that will disagree.
/// </summary>
public sealed class CookiePolicy(bool crossSite)
{
    /// <summary>
    /// Only the auth endpoints ever need the refresh token, so it is scoped to them. A cookie
    /// sent on every request to every path is one with far more chances to be logged, proxied
    /// or leaked than it has reasons to exist.
    /// </summary>
    private const string RefreshPath = "/api/auth";

    public CookieOptions For(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        // Lax blocks the cookie on a cross-site POST, which is CSRF protection for free when
        // both halves are served from one origin. A split-origin development setup cannot use
        // it, so that is an explicit switch rather than something guessed from a hostname.
        SameSite = crossSite ? SameSiteMode.None : SameSiteMode.Lax,
        Expires = expires,
        Path = RefreshPath,
        IsEssential = true,
    };

    /// <summary>
    /// Deleting a cookie only works when the flags match the ones it was written with, so this
    /// is derived from the same place rather than written out again.
    /// </summary>
    public CookieOptions Deletion() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = crossSite ? SameSiteMode.None : SameSiteMode.Lax,
        Path = RefreshPath,
        IsEssential = true,
    };
}
