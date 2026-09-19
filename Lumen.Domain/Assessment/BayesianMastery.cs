namespace Lumen.Domain.Assessment;

/// <param name="Prior">Belief a student already knows a concept before any evidence.</param>
/// <param name="Learn">Chance a concept is learned between one check and the next.</param>
/// <param name="Guess">Chance of answering correctly without knowing it.</param>
/// <param name="Slip">Chance of answering wrongly while knowing it.</param>
public sealed record MasteryParameters(double Prior, double Learn, double Guess, double Slip)
{
    /// <summary>
    /// Guess is set high because these are spoken, open questions in a conversation — a student
    /// can half-recall a phrase from thirty seconds ago and sound right. Slip is lower but not
    /// negligible: people fumble answers they know, especially out loud.
    /// </summary>
    public static readonly MasteryParameters Default = new(Prior: 0.25, Learn: 0.15, Guess: 0.20, Slip: 0.10);

    public bool IsUsable =>
        Prior is > 0 and < 1 && Learn is >= 0 and < 1 && Guess is > 0 and < 1 && Slip is > 0 and < 1;
}

/// <summary>
/// How belief that a student knows a concept moves as evidence arrives.
///
/// Bayesian knowledge tracing, which is the standard model for this and is worth the arithmetic
/// over a running average for one reason: it takes guessing and slipping seriously. A single
/// right answer is weak evidence when a fifth of right answers are luck, and a single wrong one
/// is not proof of ignorance when people fumble things they know. An average treats both as
/// gospel and lurches around.
///
/// Pure, so the thing that decides whether a student moves on can actually be tested — unlike
/// the judgement of the answer itself, which cannot.
/// </summary>
public static class BayesianMastery
{
    /// <summary>
    /// Belief required to call a concept learned and move on.
    ///
    /// Lower than the 0.95 the literature usually quotes, and deliberately: 0.95 assumes a drill
    /// with many items per skill, where evidence is cheap. A conversation affords two or three
    /// checks per concept, so demanding 0.95 would mean a student never advances no matter how
    /// well they answer. Two clean answers from a cold start clear this; one does not.
    /// </summary>
    public const double MasteredAt = 0.85;

    /// <summary>
    /// How much of a correct answer's movement a partial answer earns. Some credit, because
    /// they have some of it — not full, because the gap is the thing still to teach.
    /// </summary>
    public const double PartialCredit = 0.4;

    public static double Observe(double belief, Verdict verdict, MasteryParameters? parameters = null)
    {
        var p = parameters ?? MasteryParameters.Default;
        if (!p.IsUsable) throw new ArgumentOutOfRangeException(nameof(parameters), "Every parameter must sit strictly between 0 and 1.");

        var prior = Math.Clamp(belief, 0, 1);

        return verdict switch
        {
            // Silence is not evidence. A student who did not answer has told us nothing about
            // what they know, and moving the estimate on it would punish thinking out loud.
            Verdict.NoAnswer => prior,

            Verdict.Correct => Transit(PosteriorAfterCorrect(prior, p), p),
            Verdict.Incorrect => Transit(PosteriorAfterIncorrect(prior, p), p),

            Verdict.Partial => Damped(prior, Transit(PosteriorAfterCorrect(prior, p), p)),

            _ => prior
        };
    }

    public static bool IsMastered(double belief) => belief >= MasteredAt;

    private static double PosteriorAfterCorrect(double prior, MasteryParameters p)
    {
        var knew = prior * (1 - p.Slip);
        var guessed = (1 - prior) * p.Guess;
        return knew + guessed <= 0 ? prior : knew / (knew + guessed);
    }

    private static double PosteriorAfterIncorrect(double prior, MasteryParameters p)
    {
        var slipped = prior * p.Slip;
        var missed = (1 - prior) * (1 - p.Guess);
        return slipped + missed <= 0 ? prior : slipped / (slipped + missed);
    }

    /// <summary>Teaching happens between checks, so belief rises a little regardless.</summary>
    private static double Transit(double posterior, MasteryParameters p) =>
        Math.Clamp(posterior + (1 - posterior) * p.Learn, 0, 1);

    private static double Damped(double prior, double full) =>
        Math.Clamp(prior + (full - prior) * PartialCredit, 0, 1);
}
