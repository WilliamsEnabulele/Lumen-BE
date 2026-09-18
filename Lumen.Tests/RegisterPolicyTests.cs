using Lumen.Domain.Scripts;
using Lumen.Domain.Teaching;

namespace Lumen.Tests;

/// <summary>
/// The rule these tests encode is one line: the interjection carries the affect, the
/// explanation carries the content. Every refusal below follows from it.
///
/// They are refusals rather than preferences because of the failure mode. A machine
/// over-performing a register is a caricature, and a caricature of how someone speaks is
/// worse than plain English by a wide margin — so the policy fails closed.
/// </summary>
public class RegisterPolicyTests
{
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(120);

    private static Interjection NaSo() => new()
    {
        Text = "Na so!",
        Function = InterjectionFunction.Encouragement,
        MinimumLevel = RegisterLevel.LightInterjection,
        AudioKeys = ["interjections/na-so/take-1", "interjections/na-so/take-2"]
    };

    private static ScriptNode Node(
        ScriptNodeKind kind = ScriptNodeKind.Speech,
        bool carriesDefinition = false) => new()
    {
        Kind = kind,
        CarriesDefinition = carriesDefinition,
        Text = "Every turn of the outer loop runs the whole inner one."
    };

    private static InterjectionDecision Decide(
        RegisterLevel level,
        ScriptNode? node = null,
        Interjection? interjection = null,
        TimeSpan? since = null,
        bool nodeAlreadyCarriesOne = false) =>
        RegisterPolicy.Decide(level, node ?? Node(), interjection ?? NaSo(), since ?? LongEnough, nodeAlreadyCarriesOne);

    [Fact]
    public void A_student_on_standard_english_never_hears_one()
    {
        var decision = Decide(RegisterLevel.StandardEnglish);

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Refusal);
        Assert.Contains("standard English", decision.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Never_during_an_assessment_item()
    {
        // A formal question asked in an informal register changes what is being asked.
        var decision = Decide(RegisterLevel.ComfortableCodeSwitch, Node(ScriptNodeKind.CheckForUnderstanding));

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Refusal);
        Assert.Contains("assessment", decision.Refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Never_inside_a_technical_definition()
    {
        // The student is examined in standard English, so the terms stay precise.
        var decision = Decide(RegisterLevel.ComfortableCodeSwitch, Node(carriesDefinition: true));

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Refusal);
        Assert.Contains("definition", decision.Refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Never_twice_in_the_same_breath()
    {
        var decision = Decide(RegisterLevel.ComfortableCodeSwitch, nodeAlreadyCarriesOne: true);

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Refusal);
        Assert.Contains("already carries", decision.Refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rate_limited_so_it_stays_conversational_rather_than_constant()
    {
        var decision = Decide(RegisterLevel.ComfortableCodeSwitch, since: TimeSpan.FromSeconds(10));

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Refusal);
        Assert.Contains("since the last one", decision.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void An_interjection_above_the_students_level_is_not_selected()
    {
        var familiar = NaSo();
        familiar.MinimumLevel = RegisterLevel.ComfortableCodeSwitch;

        var decision = Decide(RegisterLevel.LightInterjection, interjection: familiar);

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Refusal);
        Assert.Contains("needs register level", decision.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_recorded_take_it_is_not_said_at_all()
    {
        // A general-purpose voice reading Pidgin phonetically is not a degraded version of
        // this feature. It is the failure the whole policy exists to prevent.
        var unvoiced = NaSo();
        unvoiced.AudioKeys.Clear();

        var decision = Decide(RegisterLevel.ComfortableCodeSwitch, interjection: unvoiced);

        Assert.False(decision.Allowed);
        Assert.NotNull(decision.Refusal);
        Assert.Contains("recorded take", decision.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void At_the_edge_of_an_explanation_for_a_student_who_invited_it_it_lands()
    {
        var decision = Decide(RegisterLevel.LightInterjection);

        Assert.True(decision.Allowed);
        Assert.Null(decision.Refusal);
    }
}
