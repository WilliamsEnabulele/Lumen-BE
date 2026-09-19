using Lumen.Domain.Assessment;
using Lumen.Domain.Canvas;
using Lumen.Domain.Teaching;
using Lumen.Infrastructure.Storage;

namespace Lumen.Tests;

/// <summary>
/// Whether a lesson survives the process it started in.
///
/// These are written against a real temporary directory rather than a fake, because everything
/// that can go wrong here goes wrong at the boundary: a polymorphic canvas that serialises and
/// will not come back, a concept key with a slash in it, an index rebuilt from files that were
/// never written. A fake store would pass all of it.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lumen-tests-{Guid.CreateVersion7():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_lesson_in_progress_comes_back_where_it_was()
    {
        var session = new TeachingSession
        {
            CourseId = Guid.CreateVersion7(),
            StudentId = Guid.CreateVersion7(),
            LessonIndex = 2,
            ConceptIndex = 1,
            TurnsOnConcept = 4,
            Register = RegisterLevel.LightInterjection,
            AwaitingCheckAnswer = true,
            PendingQuestion = "How many times does the body run?",
        };
        session.Record(TutorTurn.FromTutor("A loop is a promise."));
        session.Record(TutorTurn.FromStudent("abeg, say that again"));

        new FileSessionStore(_root).Save(session);

        // A fresh store, as if the process had died and come back.
        var restored = new FileSessionStore(_root).Find(session.Id);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.LessonIndex);
        Assert.Equal(1, restored.ConceptIndex);
        Assert.Equal(RegisterLevel.LightInterjection, restored.Register);
        Assert.True(restored.AwaitingCheckAnswer);
        Assert.Equal("How many times does the body run?", restored.PendingQuestion);
        Assert.Equal(2, restored.History.Count);
        Assert.Equal("abeg, say that again", restored.History[1].Text);
    }

    /// <summary>
    /// Every shape the tutor can leave on the canvas, through disk and back.
    ///
    /// The canvas is a polymorphic hierarchy, which is the thing that quietly fails: a command
    /// serialises without complaint and comes back as its base type, or as nothing. Asserted
    /// field by field rather than by record equality, because a record holding a list compares
    /// that list by reference — so two identical diagrams are unequal, and an equality check
    /// here would fail on correct code and pass on nothing.
    ///
    /// <c>ClearCanvas</c> is absent on purpose: applying it empties the canvas, so it is never
    /// something the canvas holds and never something to store.
    /// </summary>
    [Fact]
    public void Everything_the_tutor_can_draw_survives_the_round_trip()
    {
        CanvasCommand[] shapes =
        [
            new ShowStatement("A loop is a promise."),
            new ShowSteps("Three parts", ["start", "test", "step"]),
            new ShowCode("python", "for i in range(5):\n    print(i)", 1),
            new ShowDiagram("Flow", [new DiagramNode("a", "Start"), new DiagramNode("b", "Body")],
                [new DiagramEdge("a", "b", "enter")]),
            new ShowChart("bar", "Runs", [new ChartPoint("first", 5), new ChartPoint("second", 25)]),
            new ShowMath("n - 1", "the highest index"),
        ];

        foreach (var drawn in shapes)
        {
            var session = new TeachingSession { Canvas = CanvasState.Empty.Apply(drawn) };
            new FileSessionStore(_root).Save(session);

            var current = new FileSessionStore(_root).Find(session.Id)?.Canvas.Current;

            Assert.NotNull(current);
            Assert.Equal(drawn.Tool, current.Tool);

            switch (drawn)
            {
                case ShowStatement statement:
                    Assert.Equal(statement.Text, Assert.IsType<ShowStatement>(current).Text);
                    break;

                case ShowSteps steps:
                    Assert.Equal(steps.Items, Assert.IsType<ShowSteps>(current).Items);
                    break;

                case ShowCode code:
                    var restoredCode = Assert.IsType<ShowCode>(current);
                    Assert.Equal(code.Source, restoredCode.Source);
                    Assert.Equal(code.HighlightLine, restoredCode.HighlightLine);
                    break;

                case ShowDiagram diagram:
                    var restoredDiagram = Assert.IsType<ShowDiagram>(current);
                    Assert.Equal(diagram.Nodes, restoredDiagram.Nodes);
                    Assert.Equal(diagram.Edges, restoredDiagram.Edges);
                    break;

                case ShowChart chart:
                    Assert.Equal(chart.Points, Assert.IsType<ShowChart>(current).Points);
                    break;

                case ShowMath math:
                    Assert.Equal(math.Latex, Assert.IsType<ShowMath>(current).Latex);
                    break;
            }
        }
    }

    [Fact]
    public void An_empty_canvas_is_still_a_canvas_after_a_restart()
    {
        var session = new TeachingSession();

        new FileSessionStore(_root).Save(session);
        var restored = new FileSessionStore(_root).Find(session.Id);

        Assert.NotNull(restored);
        Assert.Null(restored.Canvas.Current);
    }

    [Fact]
    public void A_highlight_restores_onto_the_code_it_was_moved_over()
    {
        // The one canvas command that mutates rather than replaces. If it is stored as a
        // highlight rather than as the code it landed on, the canvas comes back almost empty.
        var session = new TeachingSession
        {
            Canvas = CanvasState.Empty
                .Apply(new ShowCode("python", "a\nb\nc", 1))
                .Apply(new HighlightCode(3)),
        };

        new FileSessionStore(_root).Save(session);
        var restored = new FileSessionStore(_root).Find(session.Id)!;

        var code = Assert.IsType<ShowCode>(restored.Canvas.Current);
        Assert.Equal("a\nb\nc", code.Source);
        Assert.Equal(3, code.HighlightLine);
    }

    [Fact]
    public void What_a_student_demonstrated_outlives_the_server()
    {
        var student = Guid.CreateVersion7();
        var course = Guid.CreateVersion7();
        var store = new FileMasteryStore(_root);

        var record = store.For(student, course, "loops/counted", "Counted loops");
        record.Record(new AnswerJudgement(Verdict.Correct, null), "how many times?", "five");
        record.Record(new AnswerJudgement(Verdict.Correct, null), "and the last index?", "four");
        store.Save(record);

        var restored = new FileMasteryStore(_root).Find(student, course, "loops/counted");

        Assert.NotNull(restored);
        Assert.True(restored.IsMastered);
        Assert.Equal(2, restored.Evidence.Count);
        Assert.Equal("four", restored.Evidence[1].Answer);
    }

    [Fact]
    public void A_concept_key_with_a_slash_in_it_is_not_a_path()
    {
        // Keys are derived from concept titles, and titles contain slashes, colons and worse.
        // Filing under the key rather than the record id would write outside the folder.
        var student = Guid.CreateVersion7();
        var course = Guid.CreateVersion7();
        var store = new FileMasteryStore(_root);

        var record = store.For(student, course, "../../etc/loops: the basics", "Loops: the basics");
        record.Record(new AnswerJudgement(Verdict.Partial, "half of it"), "q", "a");
        store.Save(record);

        Assert.NotNull(new FileMasteryStore(_root).Find(student, course, "../../etc/loops: the basics"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "mastery"), "*.json"));
    }

    [Fact]
    public void A_record_nobody_answered_into_is_not_written()
    {
        var store = new FileMasteryStore(_root);
        store.For(Guid.CreateVersion7(), Guid.CreateVersion7(), "untouched", "Untouched");

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "mastery"), "*.json"));
    }

    [Fact]
    public void One_unreadable_file_does_not_stop_the_server_starting()
    {
        var session = new TeachingSession();
        new FileSessionStore(_root).Save(session);
        File.WriteAllText(Path.Combine(_root, "sessions", $"{Guid.CreateVersion7()}.json"), "{ not json");

        var store = new FileSessionStore(_root);

        Assert.NotNull(store.Find(session.Id));
    }
}
