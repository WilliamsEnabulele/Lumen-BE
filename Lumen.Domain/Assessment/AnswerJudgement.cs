namespace Lumen.Domain.Assessment;

/// <summary>
/// What a student's answer to a comprehension check was worth.
///
/// Four outcomes rather than right/wrong, because a spoken answer is rarely either. The one
/// that matters most is <see cref="NoAnswer"/>: a student who said "hmm, hang on" or asked a
/// question back has told you nothing about whether they know this, and treating that as
/// failure would punish thinking out loud — which is the behaviour a voice tutor most wants.
/// </summary>
public enum Verdict
{
    /// <summary>They have it.</summary>
    Correct,

    /// <summary>Some of it, with a gap worth naming.</summary>
    Partial,

    /// <summary>They do not have it.</summary>
    Incorrect,

    /// <summary>Not an attempt at the question. Carries no information either way.</summary>
    NoAnswer
}

/// <param name="Misconception">
/// What they appear to believe instead, when that is legible. Kept because it is the only part
/// of this worth showing an instructor — a cohort making the same wrong turn is a fact about
/// the material, not about the students.
/// </param>
public sealed record AnswerJudgement(Verdict Verdict, string? Misconception)
{
    /// <summary>Nothing was judged — no judge configured, or the call failed.</summary>
    public static readonly AnswerJudgement Unknown = new(Verdict.NoAnswer, null);

    public bool CarriesEvidence => Verdict is not Verdict.NoAnswer;
}

/// <summary>
/// Judges a spoken answer against the concept it was asked about.
///
/// Deliberately narrow. This decides only whether the answer was right — not what to do about
/// it. Every consequence of that judgement lives in <see cref="AdaptiveDecision"/>, where it
/// can be reasoned about and tested rather than buried in a generation.
/// </summary>
public interface IAnswerJudge
{
    Task<AnswerJudgement> JudgeAsync(
        string conceptTitle,
        string sourceExcerpt,
        string question,
        string studentAnswer,
        CancellationToken cancellationToken = default);
}
