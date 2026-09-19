using Lumen.Domain.Assessment;

namespace Lumen.Tests;

/// <summary>
/// The number that decides whether a student is taught something again. It is worth the
/// arithmetic of knowledge tracing over a running average for one reason: a fifth of right
/// answers are luck and people fumble things they know, and an average treats both as gospel.
/// </summary>
public class BayesianMasteryTests
{
    private const double Prior = 0.25;

    [Fact]
    public void One_right_answer_is_encouraging_but_not_proof()
    {
        var after = BayesianMastery.Observe(Prior, Verdict.Correct);

        Assert.True(after > Prior, "a correct answer should raise belief");
        Assert.False(BayesianMastery.IsMastered(after), "one answer, a fifth of which are guesses, is not mastery");
    }

    [Fact]
    public void Two_clean_answers_from_cold_are_enough_to_move_on()
    {
        // The threshold is tuned for a conversation, which affords two or three checks per
        // concept — not a drill with twenty.
        var after = BayesianMastery.Observe(BayesianMastery.Observe(Prior, Verdict.Correct), Verdict.Correct);

        Assert.True(BayesianMastery.IsMastered(after), $"belief was {after:P1}");
    }

    [Fact]
    public void A_wrong_answer_costs_more_than_a_right_one_gains()
    {
        var up = BayesianMastery.Observe(Prior, Verdict.Correct) - Prior;
        var down = Prior - BayesianMastery.Observe(Prior, Verdict.Incorrect);

        Assert.True(down < up, "from a low prior there is further to climb than to fall");
        Assert.True(BayesianMastery.Observe(Prior, Verdict.Incorrect) < Prior);
    }

    [Fact]
    public void Silence_is_not_evidence()
    {
        // A student who said "hmm, hang on" has told us nothing. Moving the estimate on that
        // would punish thinking out loud, which is exactly what a voice tutor wants to hear.
        Assert.Equal(Prior, BayesianMastery.Observe(Prior, Verdict.NoAnswer));
    }

    [Fact]
    public void A_partial_answer_earns_part_of_the_credit()
    {
        var full = BayesianMastery.Observe(Prior, Verdict.Correct);
        var partial = BayesianMastery.Observe(Prior, Verdict.Partial);

        Assert.True(partial > Prior);
        Assert.True(partial < full);
    }

    [Fact]
    public void Belief_never_leaves_its_bounds()
    {
        var belief = 0.5;
        for (var i = 0; i < 50; i++) belief = BayesianMastery.Observe(belief, Verdict.Correct);
        Assert.InRange(belief, 0, 1);

        for (var i = 0; i < 50; i++) belief = BayesianMastery.Observe(belief, Verdict.Incorrect);
        Assert.InRange(belief, 0, 1);
    }

    [Fact]
    public void A_run_of_wrong_answers_does_not_reach_certainty_either_way()
    {
        // Slip means a wrong answer is never proof of ignorance, so belief must not collapse to
        // zero and strand the student below any threshold forever.
        var belief = Prior;
        for (var i = 0; i < 10; i++) belief = BayesianMastery.Observe(belief, Verdict.Incorrect);

        Assert.True(belief > 0, "belief should not collapse to certainty");
    }

    [Fact]
    public void Nonsense_parameters_are_refused_rather_than_producing_a_nonsense_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BayesianMastery.Observe(0.5, Verdict.Correct, new MasteryParameters(0.25, 0.15, Guess: 0, Slip: 0.1)));
    }
}

public class AdaptiveDecisionTests
{
    private static MasteryRecord Record(double belief = 0.25, int reteaches = 0, int evidence = 0)
    {
        var record = new MasteryRecord { Belief = belief, Reteaches = reteaches, ConceptTitle = "Nesting" };
        for (var i = 0; i < evidence; i++)
        {
            record.Evidence.Add(new MasteryEvidence(DateTimeOffset.UtcNow, Verdict.Correct, "q", "a", null, belief));
        }
        return record;
    }

    private static AnswerJudgement Said(Verdict verdict, string? misconception = null) => new(verdict, misconception);

    [Fact]
    public void A_wrong_answer_reteaches()
    {
        var outcome = AdaptiveDecision.Decide(Record(), Said(Verdict.Incorrect));

        Assert.Equal(Adaptation.Reteach, outcome.Adaptation);
    }

    [Fact]
    public void Nobody_is_trapped_in_a_concept_they_cannot_get()
    {
        // Past a point, being told the same thing differently stops helping and the student is
        // being held rather than taught.
        var outcome = AdaptiveDecision.Decide(
            Record(reteaches: AdaptiveDecision.MaxReteaches), Said(Verdict.Incorrect));

        Assert.Equal(Adaptation.MoveOnUnmastered, outcome.Adaptation);
        Assert.Contains("flagging", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Silence_keeps_the_lesson_where_it_is()
    {
        var outcome = AdaptiveDecision.Decide(Record(), Said(Verdict.NoAnswer));

        Assert.Equal(Adaptation.Continue, outcome.Adaptation);
    }

    [Fact]
    public void A_right_answer_with_enough_belief_behind_it_advances()
    {
        var outcome = AdaptiveDecision.Decide(Record(belief: 0.92), Said(Verdict.Correct));

        Assert.Equal(Adaptation.Advance, outcome.Adaptation);
    }

    [Fact]
    public void A_right_answer_without_enough_behind_it_asks_once_more()
    {
        var outcome = AdaptiveDecision.Decide(Record(belief: 0.6), Said(Verdict.Correct));

        Assert.Equal(Adaptation.Continue, outcome.Adaptation);
    }

    [Fact]
    public void A_partial_answer_stays_on_the_gap()
    {
        var outcome = AdaptiveDecision.Decide(Record(belief: 0.6), Said(Verdict.Partial));

        Assert.Equal(Adaptation.Continue, outcome.Adaptation);
        Assert.Contains("gap", outcome.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_concept_already_demonstrated_is_skipped()
    {
        Assert.True(AdaptiveDecision.ShouldSkip(Record(belief: 0.95, evidence: 2)));
    }

    [Fact]
    public void A_cold_prior_is_never_mistaken_for_knowing_it()
    {
        // Skipping on a belief nobody ever answered into would silently drop material.
        Assert.False(AdaptiveDecision.ShouldSkip(Record(belief: 0.99, evidence: 0)));
        Assert.False(AdaptiveDecision.ShouldSkip(null));
    }
}

public class MasteryRecordTests
{
    [Fact]
    public void Recording_an_answer_moves_belief_and_leaves_a_trail()
    {
        var record = new MasteryRecord { ConceptTitle = "Nesting" };
        var before = record.Belief;

        record.Record(new AnswerJudgement(Verdict.Correct, null), "how many times?", "a hundred");

        Assert.True(record.Belief > before);
        Assert.Single(record.Evidence);
        Assert.Equal("a hundred", record.Evidence[0].Answer);
        Assert.Equal(record.Belief, record.Evidence[0].BeliefAfter);
    }

    [Fact]
    public void A_misconception_is_kept_because_it_is_the_part_worth_showing_an_instructor()
    {
        var record = new MasteryRecord();

        record.Record(new AnswerJudgement(Verdict.Incorrect, "thinks the counts add"), "how many?", "twenty");

        Assert.Equal("thinks the counts add", record.Evidence[0].Misconception);
    }

    [Fact]
    public void A_non_answer_leaves_no_trail_at_all()
    {
        // A trail full of non-answers reads as a struggling student when nothing was observed.
        var record = new MasteryRecord();

        record.Record(AnswerJudgement.Unknown, "how many?", "hmm, hang on");

        Assert.Empty(record.Evidence);
        Assert.Equal(MasteryParameters.Default.Prior, record.Belief);
    }
}

/// <summary>
/// Reading the marked reply. Every unreadable case must land on "no answer", because the
/// alternative is costing a student a mark for a model's formatting.
/// </summary>
public class JudgementReadingTests
{
    [Theory]
    [InlineData("correct", Verdict.Correct)]
    [InlineData("  Correct  ", Verdict.Correct)]
    [InlineData("PARTIAL", Verdict.Partial)]
    [InlineData("incorrect", Verdict.Incorrect)]
    [InlineData("no_answer", Verdict.NoAnswer)]
    public void A_verdict_is_read_whatever_case_it_arrives_in(string word, Verdict expected)
    {
        Assert.Equal(expected, JudgePrompt.Read(word));
    }

    [Theory]
    [InlineData("nearly")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unrecognised_verdict_costs_the_student_nothing(string? word)
    {
        Assert.Equal(Verdict.NoAnswer, JudgePrompt.Read(word));
    }

    [Fact]
    public void A_marked_answer_carries_its_misconception()
    {
        var judgement = JudgePrompt.ReadJudgement(
            """{"verdict":"incorrect","misconception":"thinks the counts add rather than multiply"}""");

        Assert.Equal(Verdict.Incorrect, judgement.Verdict);
        Assert.Equal("thinks the counts add rather than multiply", judgement.Misconception);
    }

    [Fact]
    public void A_null_misconception_stays_null_rather_than_becoming_empty_text()
    {
        var judgement = JudgePrompt.ReadJudgement("""{"verdict":"correct","misconception":null}""");

        Assert.Equal(Verdict.Correct, judgement.Verdict);
        Assert.Null(judgement.Misconception);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("")]
    public void Anything_unreadable_moves_no_estimate(string json)
    {
        var judgement = JudgePrompt.ReadJudgement(json);

        Assert.Equal(Verdict.NoAnswer, judgement.Verdict);
        Assert.False(judgement.CarriesEvidence);
    }

    [Fact]
    public void The_marking_rules_insist_on_substance_over_phrasing()
    {
        // The whole prompt bends this way; if that instruction goes, the estimate starts
        // rewarding students who memorised the wording over ones who understood it.
        Assert.Contains("never the phrasing", JudgePrompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no_answer", JudgePrompt.System, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_actually_asked_is_what_the_answer_is_marked_against()
    {
        var prompt = JudgePrompt.User("Nesting", "the outer loop runs the inner in full", "how many times?", "a hundred");

        Assert.Contains("how many times?", prompt, StringComparison.Ordinal);
        Assert.Contains("a hundred", prompt, StringComparison.Ordinal);
        Assert.Contains("the outer loop runs the inner in full", prompt, StringComparison.Ordinal);
    }
}
