using Lumen.Domain.Canvas;
using Lumen.Domain.Teaching;

namespace Lumen.Tests;

/// <summary>
/// The prompt is product, not configuration. Almost everything that makes generated teaching
/// sound like a machine is decided in it, so the things that must be in it are asserted.
/// </summary>
public class TutorPromptTests
{
    private static PlannedConcept Concept(string? visualHint = null) => new(
        Title: "Nested loops",
        TeachingIntent: "Get across that nesting multiplies the work rather than adding to it.",
        Prerequisites: ["Loops"],
        SourceRef: "ch4:p63",
        SourceExcerpt: "The outer loop takes ten turns, and each one runs the inner loop in full.",
        VisualHint: visualHint);

    private static TutorContext Context(
        CanvasState? canvas = null,
        RegisterLevel register = RegisterLevel.StandardEnglish) => new(
        CourseTitle: "Introduction to Programming",
        Lesson: new PlannedLesson("Loops", "Work out the cost of a nested loop", [Concept()]),
        Concept: Concept(),
        Canvas: canvas ?? CanvasState.Empty,
        Register: register,
        History: []);

    [Fact]
    public void The_tutor_is_grounded_in_the_passage_verbatim()
    {
        var prompt = TutorPrompt.System(Context(), TutorIntent.Teach);

        Assert.Contains("The outer loop takes ten turns", prompt, StringComparison.Ordinal);
        Assert.Contains("outside this material", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_tutor_is_told_what_the_student_can_actually_see()
    {
        // Otherwise it says "as you can see here" about a diagram it cleared two turns ago.
        var canvas = CanvasState.Empty.Apply(new ShowCode("python", "a\nb\nc", 2));

        var prompt = TutorPrompt.System(Context(canvas), TutorIntent.Teach);

        Assert.Contains("3 lines", prompt, StringComparison.Ordinal);
        Assert.Contains("line 2", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_canvas_is_stated_rather_than_left_unsaid()
    {
        Assert.Contains("canvas is empty", TutorPrompt.System(Context(), TutorIntent.Teach), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Turns_are_kept_short_enough_to_interrupt()
    {
        var prompt = TutorPrompt.System(Context(), TutorIntent.Teach);

        Assert.Contains($"at most {TutorPrompt.MaxSentencesPerTurn} sentences", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("let me explain")]
    [InlineData("great question")]
    public void The_tells_that_make_a_tutor_sound_generated_are_ruled_out(string tell)
    {
        Assert.Contains(tell, TutorPrompt.System(Context(), TutorIntent.Teach), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Each_intent_asks_for_something_different()
    {
        var context = Context();

        var teach = TutorPrompt.System(context, TutorIntent.Teach);
        var respond = TutorPrompt.System(context, TutorIntent.Respond);
        var check = TutorPrompt.System(context, TutorIntent.CheckUnderstanding);
        var reteach = TutorPrompt.System(context, TutorIntent.Reteach);

        Assert.NotEqual(teach, respond);
        Assert.NotEqual(check, reteach);
        Assert.Contains("cut in", respond, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not answer it yourself", check, StringComparison.Ordinal);
        Assert.Contains("different angle", reteach, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Standard_english_says_nothing_about_pidgin()
    {
        var prompt = TutorPrompt.System(Context(register: RegisterLevel.StandardEnglish), TutorIntent.Teach);

        Assert.DoesNotContain("Pidgin", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RegisterLevel.LightInterjection)]
    [InlineData(RegisterLevel.ComfortableCodeSwitch)]
    public void Code_switching_stays_in_the_affect_and_out_of_the_content(RegisterLevel register)
    {
        var prompt = TutorPrompt.System(Context(register: register), TutorIntent.Teach);

        Assert.Contains("Pidgin", prompt, StringComparison.OrdinalIgnoreCase);
        // The rule the whole register design rests on.
        Assert.Contains("standard English", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_visual_hint_is_offered_rather_than_ordered()
    {
        var context = Context() with { Concept = Concept("a diagram of the outer loop containing the inner") };

        var prompt = TutorPrompt.System(context, TutorIntent.Teach);

        Assert.Contains("if it fits", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_canvas_is_not_for_decoration()
    {
        var prompt = TutorPrompt.System(Context(), TutorIntent.Teach);

        Assert.Contains("Most turns need nothing", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rules_block_is_identical_whatever_is_being_taught()
    {
        // It sits behind the first cache breakpoint, which is only worth having if it is
        // byte-identical across every student and every course.
        var somewhereElse = new TutorContext(
            "A Different Course",
            new PlannedLesson("Other", "something else", [Concept()]),
            Concept("a diagram"),
            CanvasState.Empty.Apply(new ShowStatement("anything")),
            RegisterLevel.ComfortableCodeSwitch,
            [TutorTurn.FromStudent("hello")]);

        Assert.Equal(TutorPrompt.Rules, TutorPrompt.System(somewhereElse, TutorIntent.Teach)[..TutorPrompt.Rules.Length]);
        Assert.StartsWith(TutorPrompt.Rules, TutorPrompt.System(Context(), TutorIntent.Reteach), StringComparison.Ordinal);
    }

    [Fact]
    public void The_material_block_does_not_move_when_the_canvas_does()
    {
        // It sits behind the second breakpoint and must hold for the whole concept. A canvas
        // change mid-concept invalidating it would throw the cache away several times a minute.
        var before = TutorPrompt.Material(Context());
        var after = TutorPrompt.Material(Context(CanvasState.Empty.Apply(new ShowCode("py", "a\nb", 2))));

        Assert.Equal(before, after);
    }

    [Fact]
    public void Everything_that_changes_per_turn_is_in_the_last_block()
    {
        // Anything volatile above a breakpoint silently invalidates everything after it, so the
        // canvas, the register and the intent all have to live here.
        var now = TutorPrompt.Now(Context(CanvasState.Empty.Apply(new ShowCode("py", "a\nb\nc", 2))), TutorIntent.Respond);

        Assert.Contains("3 lines", now, StringComparison.Ordinal);
        Assert.Contains("cut in", now, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Concept().SourceExcerpt, now, StringComparison.Ordinal);
    }

    [Fact]
    public void The_three_blocks_together_are_the_whole_prompt()
    {
        var context = Context();

        var whole = TutorPrompt.System(context, TutorIntent.Teach);

        Assert.Contains(TutorPrompt.Material(context).Trim(), whole, StringComparison.Ordinal);
        Assert.Contains(TutorPrompt.Now(context, TutorIntent.Teach).Trim(), whole, StringComparison.Ordinal);
    }

    [Fact]
    public void History_is_bounded_so_cost_is_too()
    {
        var turns = Enumerable.Range(1, TutorContext.HistoryWindow * 2)
            .Select(i => TutorTurn.FromStudent($"turn {i}"))
            .ToArray();

        var context = Context() with { History = turns };

        Assert.Equal(TutorContext.HistoryWindow, context.RecentHistory.Count);
        Assert.Equal($"turn {turns.Length}", context.RecentHistory[^1].Text);
    }
}
