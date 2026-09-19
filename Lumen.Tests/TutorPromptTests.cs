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
