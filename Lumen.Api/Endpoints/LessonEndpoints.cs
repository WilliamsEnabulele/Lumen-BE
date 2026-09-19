using Lumen.Domain.Canvas;
using Lumen.Domain.Teaching;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

public sealed record StartSessionRequest(Guid CourseId);

public sealed record TurnRequest(string? Said);

/// <summary>
/// Teaching, one turn at a time.
///
/// A turn is a request because a conversation is: the student says something or says nothing,
/// and the tutor answers. Nothing is prepared in advance beyond the plan, which is the whole
/// point — a tutor reading ahead cannot react to the person in front of it.
/// </summary>
public static class LessonEndpoints
{
    public static void MapLessonEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sessions", (StartSessionRequest request, ICourseStore courses, ISessionStore sessions) =>
        {
            var course = courses.Find(request.CourseId);
            if (course is null) return Results.NotFound();
            if (course.Plan.Lessons.Count == 0)
                return Results.BadRequest(new { error = "That course has no lessons to teach." });

            var session = new TeachingSession { CourseId = course.Id, CreatedAt = DateTimeOffset.UtcNow };
            sessions.Save(session);

            return Results.Ok(Describe(session, course));
        });

        app.MapPost("/api/sessions/{sessionId:guid}/turn",
            async (Guid sessionId, TurnRequest request, ICourseStore courses, ISessionStore sessions, ITutorBrain brain) =>
        {
            var session = sessions.Find(sessionId);
            if (session is null) return Results.NotFound();

            var course = courses.Find(session.CourseId);
            if (course is null) return Results.NotFound();

            if (session.Complete)
                return Results.Ok(new { said = string.Empty, drew = Array.Empty<object>(), complete = true });

            var concept = session.CurrentConcept(course.Plan);
            var lesson = session.CurrentLesson(course.Plan);
            if (concept is null || lesson is null)
            {
                session.Complete = true;
                sessions.Save(session);
                return Results.Ok(new { said = string.Empty, drew = Array.Empty<object>(), complete = true });
            }

            var said = string.IsNullOrWhiteSpace(request.Said) ? null : request.Said.Trim();
            if (said is not null) session.Record(TutorTurn.FromStudent(said));

            var intent = TurnDirector.Decide(session, said);

            var context = new TutorContext(
                course.Plan.CourseTitle, lesson, concept, session.Canvas, session.Register, session.History);

            var response = await brain.RespondAsync(context, intent, said);

            if (!string.IsNullOrWhiteSpace(response.Said)) session.Record(TutorTurn.FromTutor(response.Said));
            foreach (var command in response.Drew) session.Canvas = session.Canvas.Apply(command);

            // A check asked is a check waiting for an answer; the next thing the student says
            // is read as one rather than as an interruption.
            session.AwaitingCheckAnswer = intent == TutorIntent.CheckUnderstanding;
            if (intent == TutorIntent.Reteach) session.AwaitingReteach = false;

            if (response.ConceptComplete) session.Advance(course.Plan);

            sessions.Save(session);

            return Results.Ok(new
            {
                said = response.Said,
                drew = response.Drew.Select(Draw),
                canvas = session.Canvas.Describe(),
                conceptComplete = response.ConceptComplete,
                complete = session.Complete,
                lessonTitle = session.CurrentLesson(course.Plan)?.Title ?? lesson.Title,
                conceptTitle = session.CurrentConcept(course.Plan)?.Title ?? concept.Title,
                sourceRef = concept.SourceRef,
                tutor = brain.Name,
            });
        });
    }

    private static object Describe(TeachingSession session, StoredCourse course) => new
    {
        sessionId = session.Id,
        courseId = course.Id,
        courseTitle = course.Plan.CourseTitle,
        summary = course.Plan.Summary,
        authoredBy = course.AuthoredBy,
        lessonTitle = session.CurrentLesson(course.Plan)?.Title,
        conceptTitle = session.CurrentConcept(course.Plan)?.Title,
        lessons = course.Plan.Lessons.Select(lesson => new
        {
            title = lesson.Title,
            objective = lesson.Objective,
            concepts = lesson.Concepts.Select(concept => concept.Title),
        }),
    };

    /// <summary>The wire shape of a canvas command. Flat on purpose — the client switches on `tool`.</summary>
    internal static object Draw(CanvasCommand command) => command switch
    {
        ShowStatement statement => new { tool = command.Tool, text = statement.Text },
        ShowSteps steps => new { tool = command.Tool, title = steps.Title, items = steps.Items },
        ShowCode code => new { tool = command.Tool, language = code.Language, source = code.Source, highlightLine = code.HighlightLine },
        HighlightCode highlight => new { tool = command.Tool, line = highlight.Line },
        ShowDiagram diagram => new { tool = command.Tool, title = diagram.Title, nodes = diagram.Nodes, edges = diagram.Edges },
        ShowChart chart => new { tool = command.Tool, kind = chart.Kind, title = chart.Title, points = chart.Points },
        ShowMath math => new { tool = command.Tool, latex = math.Latex, caption = math.Caption },
        _ => new { tool = command.Tool },
    };
}
