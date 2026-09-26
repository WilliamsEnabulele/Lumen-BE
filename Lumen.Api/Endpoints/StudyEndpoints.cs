using Lumen.Api.Accounts;
using Lumen.Api.Explorer;
using Lumen.Domain.Study;
using Lumen.Infrastructure.Storage;

namespace Lumen.Api.Endpoints;

public sealed record KeepNoteRequest(
    string? Kind, string? Body, string? ConceptTitle, Guid? SessionId, string? SourceRef);

public sealed record EditNoteRequest(string? Body);

/// <summary>
/// What a student kept from a course: lines of the tutor's worth coming back to, and their own
/// notes.
///
/// Kept on the server rather than in the browser, and that is the whole point of the feature
/// rather than an implementation detail. The sign-up screen promises that a student's courses
/// and what the tutor has worked out about them follow between devices; notes that lived in
/// local storage would be the one thing on that screen that quietly did not, and the student
/// would find out by losing them.
/// </summary>
public static class StudyEndpoints
{
    public static void MapStudyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/courses/{courseId:guid}/notes", (
            Guid courseId, IStudyNoteStore notes, ICourseStore courses, ISignedIn signedIn) =>
        {
            if (signedIn.Student is not { } student)
                return Results.Json(new { error = "Sign in to see your notes." }, statusCode: 401);

            // Asked of the course store first, so somebody else's course id is not found rather
            // than found to be empty. An empty list is an answer, and the answer "that course
            // has no notes" is one this student is not entitled to.
            if (courses.FindFor(student.Id, courseId) is null) return Results.NotFound();

            return Results.Ok(notes.ListFor(student.Id, courseId).Select(View));
        })
        .WithTags(ApiTags.Study);

        app.MapPost("/api/courses/{courseId:guid}/notes", (
            Guid courseId,
            KeepNoteRequest request,
            IStudyNoteStore notes,
            ICourseStore courses,
            ISignedIn signedIn) =>
        {
            if (signedIn.Student is not { } student)
                return Results.Json(new { error = "Sign in to keep this." }, statusCode: 401);

            if (courses.FindFor(student.Id, courseId) is null) return Results.NotFound();

            if (!Enum.TryParse<NoteKind>(request.Kind, ignoreCase: true, out var kind))
                return Results.BadRequest(new { error = "A note is either a KeyPoint or a Note." });

            var kept = notes.ListFor(student.Id, courseId);
            var refusal = StudyNotes.CheckKeep(kind, request.Body, kept);

            if (refusal != NoteRefusal.None)
            {
                // Keeping the same line twice is the student's button pressed twice, not a
                // failure they need to act on, so they are handed the one they already have
                // rather than an error about it.
                if (refusal == NoteRefusal.AlreadyKept
                    && kept.FirstOrDefault(note => StudyNotes.IsSame(note, kind, request.Body!)) is { } existing)
                {
                    return Results.Ok(View(existing));
                }

                return Results.BadRequest(new { error = StudyNotes.Explain(refusal) });
            }

            var now = DateTimeOffset.UtcNow;
            var note = new StudyNote
            {
                StudentId = student.Id,
                TenantId = student.TenantId,
                CourseId = courseId,
                Kind = kind,
                Body = StudyNotes.Normalise(request.Body),
                ConceptTitle = Trimmed(request.ConceptTitle),
                SessionId = request.SessionId,
                // Only a kept line carries one. A student's own note is not a claim about the
                // material, so pointing it at a page would be inventing a citation for them.
                SourceRef = kind == NoteKind.KeyPoint ? Trimmed(request.SourceRef) : null,
                CreatedAt = now,
                UpdatedAt = now,
            };

            notes.Save(note);

            return Results.Ok(View(note));
        })
        .WithTags(ApiTags.Study);

        app.MapPut("/api/notes/{noteId:guid}", (
            Guid noteId, EditNoteRequest request, IStudyNoteStore notes, ISignedIn signedIn) =>
        {
            if (signedIn.Student is not { } student)
                return Results.Json(new { error = "Sign in to edit this." }, statusCode: 401);

            if (notes.FindFor(student.Id, noteId) is not { } note) return Results.NotFound();

            // A kept line is the tutor's words, and it carries a SourceRef saying where they
            // came from. Editing the words while keeping the citation would turn it into a
            // claim about the material that the material does not make.
            if (note.Kind == NoteKind.KeyPoint)
            {
                return Results.BadRequest(new
                {
                    error = "A key point is the tutor's own words. Write a note instead.",
                });
            }

            var body = StudyNotes.Normalise(request.Body);

            if (body.Length == 0) return Results.BadRequest(new { error = StudyNotes.Explain(NoteRefusal.Empty) });
            if (body.Length > StudyNotes.MaximumLength)
                return Results.BadRequest(new { error = StudyNotes.Explain(NoteRefusal.TooLong) });

            note.Body = body;
            note.UpdatedAt = DateTimeOffset.UtcNow;
            notes.Save(note);

            return Results.Ok(View(note));
        })
        .WithTags(ApiTags.Study);

        app.MapDelete("/api/notes/{noteId:guid}", (
            Guid noteId, IStudyNoteStore notes, ISignedIn signedIn) =>
        {
            if (signedIn.Student is not { } student)
                return Results.Json(new { error = "Sign in to delete this." }, statusCode: 401);

            if (notes.FindFor(student.Id, noteId) is not { } note) return Results.NotFound();

            notes.Delete(note);

            return Results.NoContent();
        })
        .WithTags(ApiTags.Study);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object View(StudyNote note) => new
    {
        id = note.Id,
        kind = note.Kind,
        body = note.Body,
        conceptTitle = note.ConceptTitle,
        sessionId = note.SessionId,
        sourceRef = note.SourceRef,
        createdAt = note.CreatedAt,
        updatedAt = note.UpdatedAt,
    };
}
