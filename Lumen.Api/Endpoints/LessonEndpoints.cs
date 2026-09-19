using Lumen.Domain.Assessment;
using Lumen.Domain.Canvas;
using Lumen.Domain.Courses;
using Lumen.Domain.Teaching;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

public sealed record StartSessionRequest(Guid CourseId);

public sealed record TurnRequest(string? Said);

/// <summary>
/// Teaching, one turn at a time.
///
/// A turn is a request because a conversation is: the student says something or says nothing,
/// and the tutor answers. Nothing is prepared in advance beyond the plan.
/// </summary>
public static class LessonEndpoints
{
    /// <summary>Until there is authentication, every session belongs to the same student.</summary>
    private static readonly Guid DefaultStudent = Guid.Parse("0197b9c2-0000-7000-8000-000000000002");

    public static void MapLessonEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sessions", (StartSessionRequest request, ICourseStore courses, ISessionStore sessions) =>
        {
            var course = courses.Find(request.CourseId);
            if (course is null) return Results.NotFound();
            if (course.Plan.Lessons.Count == 0)
                return Results.BadRequest(new { error = "That course has no lessons to teach." });

            var session = new TeachingSession
            {
                CourseId = course.Id,
                StudentId = DefaultStudent,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            sessions.Save(session);

            return Results.Ok(Describe(session, course));
        });

        app.MapPost("/api/sessions/{sessionId:guid}/turn", async (
            Guid sessionId,
            TurnRequest request,
            ICourseStore courses,
            ISessionStore sessions,
            IMasteryStore mastery,
            IAnswerJudge judge,
            ITutorBrain brain) =>
        {
            var session = sessions.Find(sessionId);
            if (session is null) return Results.NotFound();

            var course = courses.Find(session.CourseId);
            if (course is null) return Results.NotFound();

            if (session.Complete) return Results.Ok(Finished(session));

            var said = string.IsNullOrWhiteSpace(request.Said) ? null : request.Said.Trim();

            // A pending check is marked before anything else, because the answer can move the
            // session on — to the next concept, or back into this one from a different angle.
            var marked = await Mark(session, course, said, mastery, judge);

            // A concept that has gone nowhere for long enough is left behind and flagged. The
            // reteach cap only counts wrong answers, so without this a student who never
            // answers has no way out of the concept they are on.
            var abandoned = Abandon(session, course, mastery);

            // Then, on entry only: anything already demonstrated is not taught again.
            var skipped = ConceptEntry.SkipKnown(
                session,
                course.Plan,
                known => mastery.Find(session.StudentId, course.Id, ConceptKey.From(course.Id, known.Title)));

            if (session.Complete)
            {
                sessions.Save(session);
                return Results.Ok(Finished(session));
            }

            var concept = session.CurrentConcept(course.Plan);
            var lesson = session.CurrentLesson(course.Plan);
            if (concept is null || lesson is null)
            {
                session.Complete = true;
                sessions.Save(session);
                return Results.Ok(Finished(session));
            }

            if (said is not null) session.Record(TutorTurn.FromStudent(said));

            // An utterance already consumed as a check answer is not also an interruption.
            // Passing it on would have the tutor respond to it conversationally instead of
            // reteaching — which is how the reteach path became unreachable in the first place.
            var intent = TurnDirector.Decide(session, marked is null ? said : null);

            var context = new TutorContext(
                course.Plan.CourseTitle, lesson, concept, session.Canvas, session.Register, session.History);

            var response = await brain.RespondAsync(context, intent, said);

            if (!string.IsNullOrWhiteSpace(response.Said)) session.Record(TutorTurn.FromTutor(response.Said));
            foreach (var command in response.Drew) session.Canvas = session.Canvas.Apply(command);

            // A check asked is a check waiting for an answer, and the question is kept so the
            // answer is marked against what was actually asked.
            session.AwaitingCheckAnswer = intent == TutorIntent.CheckUnderstanding;
            session.PendingQuestion = session.AwaitingCheckAnswer ? response.Said : null;
            if (intent == TutorIntent.Reteach) session.AwaitingReteach = false;

            // The tutor saying the concept has landed is a request to move on, not a move. It
            // knows what it has said; it does not know what the student can do. So it brings
            // the check forward — the only thing that leaves a concept is evidence, an explicit
            // give-up after the reteach cap, or a stall.
            if (response.ConceptComplete) session.TutorSaysReady = true;

            sessions.Save(session);

            return Results.Ok(new
            {
                said = response.Said,
                drew = response.Drew.Select(Draw),
                canvas = session.Canvas.Describe(),
                conceptComplete = response.ConceptComplete,
                abandoned,
                skipped,
                complete = session.Complete,
                lessonTitle = session.CurrentLesson(course.Plan)?.Title ?? lesson.Title,
                conceptTitle = session.CurrentConcept(course.Plan)?.Title ?? concept.Title,
                sourceRef = concept.SourceRef,
                tutor = brain.Name,
                marked = marked is null
                    ? (object?)null
                    : new { verdict = marked.Value.Verdict.ToString(), marked.Value.Outcome.Reason },
            });
        });

        // The simplest useful report: what this student is believed to know, and the evidence.
        app.MapGet("/api/sessions/{sessionId:guid}/progress", (
            Guid sessionId, ISessionStore sessions, IMasteryStore mastery) =>
        {
            var session = sessions.Find(sessionId);
            if (session is null) return Results.NotFound();

            return Results.Ok(mastery.ForStudent(session.StudentId, session.CourseId).Select(record => new
            {
                concept = record.ConceptTitle,
                belief = Math.Round(record.Belief, 3),
                mastered = record.IsMastered,
                reteaches = record.Reteaches,
                movedOnUnmastered = record.MovedOnUnmastered,
                evidence = record.Evidence.Select(item => new
                {
                    at = item.At,
                    verdict = item.Verdict.ToString(),
                    item.Question,
                    item.Answer,
                    item.Misconception,
                    belief = Math.Round(item.BeliefAfter, 3),
                }),
            }));
        });
    }

    /// <summary>
    /// Marks a pending check, records the evidence, and applies what it implies. Returns null
    /// when there was nothing to mark, which is most turns.
    /// </summary>
    private static async Task<(Verdict Verdict, AdaptiveOutcome Outcome)?> Mark(
        TeachingSession session,
        StoredCourse course,
        string? said,
        IMasteryStore mastery,
        IAnswerJudge judge)
    {
        if (said is null || !session.AwaitingCheckAnswer) return null;
        if (session.PendingQuestion is not { Length: > 0 } question) return null;

        var concept = session.CurrentConcept(course.Plan);
        if (concept is null) return null;

        var judgement = await judge.JudgeAsync(concept.Title, concept.SourceExcerpt, question, said);

        var record = mastery.For(
            session.StudentId, course.Id, ConceptKey.From(course.Id, concept.Title), concept.Title);

        record.Record(judgement, question, said);
        var outcome = AdaptiveDecision.Decide(record, judgement);

        switch (outcome.Adaptation)
        {
            case Adaptation.Reteach:
                record.Reteaches++;
                session.AwaitingReteach = true;
                break;

            case Adaptation.MoveOnUnmastered:
                record.MovedOnUnmastered = true;
                session.Advance(course.Plan);
                break;

            case Adaptation.Advance:
                session.Advance(course.Plan);
                break;

            case Adaptation.Continue:
                // Teach a little more before asking again, rather than firing a second question
                // straight into the silence after the first. Being questioned twice in a row
                // reads as an interrogation, not a lesson.
                session.TurnsOnConcept = 0;
                break;
        }

        mastery.Save(record);
        session.AwaitingCheckAnswer = false;
        session.PendingQuestion = null;

        return (judgement.Verdict, outcome);
    }

    /// <summary>
    /// Leaves a stalled concept, flagging it as never mastered. Returns what was left, or null
    /// when nothing was — which is almost every turn.
    /// </summary>
    private static string? Abandon(TeachingSession session, StoredCourse course, IMasteryStore mastery)
    {
        if (session.Complete || !TurnDirector.HasStalled(session)) return null;

        var concept = session.CurrentConcept(course.Plan);
        if (concept is null) return null;

        var record = mastery.For(
            session.StudentId, course.Id, ConceptKey.From(course.Id, concept.Title), concept.Title);
        record.MovedOnUnmastered = true;
        mastery.Save(record);

        session.Advance(course.Plan);
        return concept.Title;
    }

    private static object Finished(TeachingSession session) => new
    {
        said = string.Empty,
        drew = Array.Empty<object>(),
        conceptComplete = true,
        complete = true,
        abandoned = (string?)null,
        skipped = Array.Empty<string>(),
        lessonTitle = string.Empty,
        conceptTitle = string.Empty,
        sourceRef = string.Empty,
        tutor = string.Empty,
        marked = (object?)null,
    };

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
