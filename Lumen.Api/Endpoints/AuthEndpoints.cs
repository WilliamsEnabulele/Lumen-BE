using Lumen.Domain.Accounts;
using Lumen.Api.Accounts;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

public sealed record SignUpRequest(string? Email, string? Password, string? Name);

public sealed record SignInRequest(string? Email, string? Password);

/// <summary>
/// Accounts.
///
/// Two rules shape everything here. A sign-in failure says the same thing whichever half was
/// wrong, because distinguishing them hands an attacker a list of real accounts and hands
/// anybody else a way to find out where a person has signed up. And the session travels in an
/// HttpOnly cookie rather than a token the page can read, so a script that gets onto the page
/// cannot walk off with somebody's account.
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

            return SignIn(student, sessions, response, cookies, now);
        })
        .RequireRateLimiting(RateLimits.Auth);

        app.MapPost("/api/auth/login", (
            SignInRequest request,
            IStudentStore students,
            IAuthSessionStore sessions,
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

            return SignIn(student, sessions, response, cookies, now);
        })
        .RequireRateLimiting(RateLimits.Auth);

        app.MapPost("/api/auth/logout", (
            ISignedIn signedIn, IAuthSessionStore sessions, HttpResponse response) =>
        {
            // Revoked server-side as well as cleared client-side. Deleting the cookie alone
            // leaves a token that still works to anybody who copied it.
            if (signedIn.Student is { } student)
                sessions.RevokeAllFor(student.Id, DateTimeOffset.UtcNow);

            response.Cookies.Delete(SignedIn.Cookie, cookies.Deletion());

            return Results.Ok(new { signedOut = true });
        });

        app.MapGet("/api/auth/me", (ISignedIn signedIn) =>
            signedIn.Student is { } student
                ? Results.Ok(new { id = student.Id, email = student.Email, name = student.Name })
                : Results.Json(new { error = "Nobody is signed in." }, statusCode: 401));
    }

    private static IResult SignIn(
        Student student,
        IAuthSessionStore sessions,
        HttpResponse response,
        CookiePolicy cookies,
        DateTimeOffset now)
    {
        var (session, token) = AuthSession.Issue(student.Id, now);
        sessions.Save(session);

        response.Cookies.Append(SignedIn.Cookie, token, cookies.For(session.ExpiresAt));

        // The token is in the cookie and nowhere else. Returning it in the body too would put
        // it somewhere a script can read, which is the whole thing HttpOnly is for.
        return Results.Ok(new { id = student.Id, email = student.Email, name = student.Name });
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
    public CookieOptions For(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        // Lax blocks the cookie on a cross-site POST, which is CSRF protection for free when
        // both halves are served from one origin. A split-origin development setup cannot use
        // it, so that is an explicit switch rather than something guessed from a hostname.
        SameSite = crossSite ? SameSiteMode.None : SameSiteMode.Lax,
        Expires = expires,
        Path = "/",
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
        Path = "/",
        IsEssential = true,
    };
}
