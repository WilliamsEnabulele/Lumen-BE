using Lumen.Api.Accounts;
using Lumen.Api.Explorer;
using Lumen.Domain.Assessment;
using Lumen.Domain.Progress;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

/// <summary>
/// What the home screen asks: where was I, what have I shown, and have I been turning up.
///
/// Three endpoints rather than one `/api/home`, because a single endpoint shaped like a screen
/// is an endpoint that changes every time the screen does, and the three questions have
/// genuinely different answers and different lifetimes.
/// </summary>
public static class HomeEndpoints
{
    public static void MapHomeEndpoints(this WebApplication app)
    {
        // Pick up where you left off. The position and the canvas are already on the session;
        // this is the read that finds it without already knowing its id.
        app.MapGet("/api/sessions", (
            ISessionStore sessions, ICourseStore courses, ISignedIn signedIn) =>
        {
            if (signedIn.Student is not { } student)
                return Results.Json(new { error = "Sign in to see your lessons." }, statusCode: 401);

            var open = sessions.ListFor(student.Id)
                .Select(session => new { session, course = courses.FindFor(student.Id, session.CourseId) })
                // A session whose course has gone is not resumable, and offering it is offering
                // a door onto nothing.
                .Where(pair => pair.course is not null)
                .Select(pair => Describe(pair.session, pair.course!))
                .ToArray();

            return Results.Ok(open);
        })
        .WithTags(ApiTags.Teaching);

        // What this student has shown, across every course rather than within one lesson.
        app.MapGet("/api/progress", (IMasteryStore mastery, ISignedIn signedIn) =>
        {
            if (signedIn.Student is not { } student)
                return Results.Json(new { error = "Sign in to see this." }, statusCode: 401);

            return Results.Ok(mastery.AllFor(student.Id).Select(record => new
            {
                concept = record.ConceptTitle,
                courseId = record.CourseId,
                belief = record.Belief,
                mastered = record.IsMastered,
                reteaches = record.Reteaches,
                movedOnUnmastered = record.MovedOnUnmastered,
                // The evidence trail is deliberately not here. This is a summary across every
                // course, and shipping every answer anybody ever gave to draw a list of
                // headings is a page that gets slower the longer somebody uses the product.
                answers = record.Evidence.Count,
            }));
        })
        .WithTags(ApiTags.Teaching);

        // Turning up, counted. The one number in the product that is shown as a number.
        app.MapGet("/api/streak", (IStudyDayStore days, ISignedIn signedIn) =>
        {
            if (signedIn.Student is not { } student)
                return Results.Json(new { error = "Sign in to see this." }, statusCode: 401);

            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
            var streak = LearningStreak.From(days.DaysFor(student.Id), today);

            return Results.Ok(new
            {
                current = streak.Current,
                longest = streak.Longest,
                thisWeek = streak.ThisWeek,
                goal = streak.Goal,
                goalMet = streak.GoalMet,
                // The days themselves, so the week can be drawn rather than described.
                days = days.DaysFor(student.Id)
                    .Where(day => day >= today.AddDays(-6))
                    .OrderBy(day => day)
                    .Select(day => day.ToString("yyyy-MM-dd")),
            });
        })
        .WithTags(ApiTags.Teaching);
    }

    private static object Describe(Lumen.Domain.Teaching.TeachingSession session, StoredCourse course)
    {
        var concepts = course.Plan.Lessons.SelectMany(lesson => lesson.Concepts).ToArray();

        // Where the lesson is, counted the same way the client counts it: concepts behind the
        // open one. Anything else and the home screen and the lesson disagree about progress.
        var behind = course.Plan.Lessons
            .Take(session.LessonIndex)
            .Sum(lesson => lesson.Concepts.Count) + session.ConceptIndex;

        var concept = behind >= 0 && behind < concepts.Length ? concepts[behind].Title : null;

        return new
        {
            sessionId = session.Id,
            courseId = course.Id,
            courseTitle = course.Plan.CourseTitle,
            conceptTitle = concept,
            conceptsBehind = Math.Clamp(behind, 0, concepts.Length),
            conceptsTotal = concepts.Length,
            lastTaughtAt = session.UpdatedAt,
        };
    }
}
