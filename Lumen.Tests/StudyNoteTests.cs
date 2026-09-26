using Lumen.Domain.Study;
using Lumen.Infrastructure.Storage;

namespace Lumen.Tests;

/// <summary>
/// The rules about keeping something from a lesson.
///
/// The asymmetry between the two kinds is the part worth pinning down: a kept line is the
/// tutor's words and keeping it twice is always a mistake, while a note is the student's own
/// and two that read the same are two thoughts they had.
/// </summary>
public class StudyNoteRuleTests
{
    private static StudyNote Kept(NoteKind kind, string body) => new() { Kind = kind, Body = body };

    [Fact]
    public void Nothing_is_kept_from_an_empty_body()
    {
        Assert.Equal(NoteRefusal.Empty, StudyNotes.CheckKeep(NoteKind.Note, "   ", []));
        Assert.Equal(NoteRefusal.Empty, StudyNotes.CheckKeep(NoteKind.Note, null, []));
    }

    [Fact]
    public void A_note_longer_than_the_cap_is_refused()
    {
        var tooLong = new string('a', StudyNotes.MaximumLength + 1);

        Assert.Equal(NoteRefusal.TooLong, StudyNotes.CheckKeep(NoteKind.Note, tooLong, []));
        Assert.Equal(NoteRefusal.None, StudyNotes.CheckKeep(NoteKind.Note, tooLong[..^1], []));
    }

    [Fact]
    public void The_same_line_is_not_kept_as_a_key_point_twice()
    {
        var already = new[] { Kept(NoteKind.KeyPoint, "Chlorophyll reflects green light.") };

        // The second press of the button does not produce a cleaner copy of the sentence.
        Assert.Equal(
            NoteRefusal.AlreadyKept,
            StudyNotes.CheckKeep(NoteKind.KeyPoint, "  chlorophyll REFLECTS green light.  ", already));
    }

    [Fact]
    public void The_same_words_written_twice_as_notes_are_two_notes()
    {
        var already = new[] { Kept(NoteKind.Note, "come back to this") };

        // Refusing this would be the server telling somebody what they meant.
        Assert.Equal(NoteRefusal.None, StudyNotes.CheckKeep(NoteKind.Note, "come back to this", already));
    }

    [Fact]
    public void A_note_and_a_key_point_reading_the_same_do_not_collide()
    {
        var already = new[] { Kept(NoteKind.Note, "water supplies electrons") };

        Assert.Equal(NoteRefusal.None, StudyNotes.CheckKeep(NoteKind.KeyPoint, "water supplies electrons", already));
    }

    [Fact]
    public void Only_the_ends_are_trimmed_and_the_students_own_layout_survives()
    {
        // A note laid out in lines is laid out in lines because somebody laid it out.
        Assert.Equal("one\n\ntwo", StudyNotes.Normalise("  one\n\ntwo \n"));
    }

    [Fact]
    public void Every_refusal_says_something_a_person_can_act_on()
    {
        foreach (var refusal in Enum.GetValues<NoteRefusal>())
        {
            if (refusal == NoteRefusal.None) continue;
            Assert.False(string.IsNullOrWhiteSpace(StudyNotes.Explain(refusal)));
        }
    }
}

/// <summary>
/// Keeping notes where they can be found again — and, more to the point, where somebody else
/// cannot find them.
/// </summary>
public class StudyNoteStoreTests
{
    private static StudyNote Note(Guid student, Guid course, string body, DateTimeOffset at) => new()
    {
        StudentId = student,
        CourseId = course,
        Kind = NoteKind.Note,
        Body = body,
        CreatedAt = at,
        UpdatedAt = at,
    };

    [Fact]
    public void Somebody_elses_note_is_not_found_by_its_id()
    {
        var store = new InMemoryStudyNoteStore();
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();
        var course = Guid.CreateVersion7();

        var note = Note(theirs, course, "theirs", DateTimeOffset.UtcNow);
        store.Save(note);

        // Holding the id is not the same as being entitled to it, and a note is the most
        // personal thing here — it is what somebody thought while they were confused.
        Assert.Null(store.FindFor(mine, note.Id));
        Assert.NotNull(store.FindFor(theirs, note.Id));
    }

    [Fact]
    public void A_list_holds_only_this_students_notes_on_this_course()
    {
        var store = new InMemoryStudyNoteStore();
        var mine = Guid.CreateVersion7();
        var theirs = Guid.CreateVersion7();
        var course = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        store.Save(Note(mine, course, "mine here", now));
        store.Save(Note(mine, other, "mine elsewhere", now));
        store.Save(Note(theirs, course, "theirs here", now));

        Assert.Equal(["mine here"], store.ListFor(mine, course).Select(note => note.Body));
    }

    [Fact]
    public void The_most_recently_kept_comes_first()
    {
        var store = new InMemoryStudyNoteStore();
        var student = Guid.CreateVersion7();
        var course = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        store.Save(Note(student, course, "first", now.AddMinutes(-10)));
        store.Save(Note(student, course, "second", now.AddMinutes(-5)));
        store.Save(Note(student, course, "third", now));

        // A list that appends to the bottom makes somebody scroll to see what they just did.
        Assert.Equal(["third", "second", "first"], store.ListFor(student, course).Select(note => note.Body));
    }

    [Fact]
    public void A_deleted_note_does_not_come_back_after_a_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lumen-notes-{Guid.CreateVersion7():N}");
        var student = Guid.CreateVersion7();
        var course = Guid.CreateVersion7();

        try
        {
            var store = new FileStudyNoteStore(root);
            var note = Note(student, course, "kept then dropped", DateTimeOffset.UtcNow);
            store.Save(note);
            store.Delete(note);

            // Removed in the app and still on disk is worse than never deleting at all: the
            // student watched it go, and then it came back.
            var reopened = new FileStudyNoteStore(root);
            Assert.Empty(reopened.ListFor(student, course));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void What_was_kept_survives_a_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lumen-notes-{Guid.CreateVersion7():N}");
        var student = Guid.CreateVersion7();
        var course = Guid.CreateVersion7();

        try
        {
            var store = new FileStudyNoteStore(root);
            store.Save(new StudyNote
            {
                StudentId = student,
                CourseId = course,
                Kind = NoteKind.KeyPoint,
                Body = "Sunlight provides the energy.",
                ConceptTitle = "Light-dependent reactions",
                SourceRef = "photosynthesis-ch3.pdf p4",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            var reopened = new FileStudyNoteStore(root).ListFor(student, course);

            Assert.Single(reopened);
            Assert.Equal(NoteKind.KeyPoint, reopened[0].Kind);
            // The citation survives with it. A claim nobody can locate is a claim nobody can
            // withdraw, and that does not stop being true once it is in somebody's revision list.
            Assert.Equal("photosynthesis-ch3.pdf p4", reopened[0].SourceRef);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
