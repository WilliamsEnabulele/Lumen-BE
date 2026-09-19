namespace Lumen.Domain.Assessment;

/// <summary>What happens to the lesson after a check.</summary>
public enum Adaptation
{
    /// <summary>Teach it again, differently. The answer was wrong.</summary>
    Reteach,

    /// <summary>Stay on this concept. Not enough yet, or nothing was said.</summary>
    Continue,

    /// <summary>They have it. Move on.</summary>
    Advance,

    /// <summary>
    /// Move on without mastery, because staying is no longer helping. Flagged for the
    /// instructor rather than hidden.
    /// </summary>
    MoveOnUnmastered
}

public sealed record AdaptiveOutcome(Adaptation Adaptation, string Reason);

/// <summary>
/// Turns a judged answer into what the lesson does next.
///
/// This is the half of assessment that must not live in the model. The judgement — was that
/// answer right — genuinely needs one. What to do about it is policy: how much evidence is
/// enough, how many times to reteach before it stops being kind, whether silence counts. Ask a
/// model to decide both and the behaviour becomes untestable and drifts with the prompt.
/// </summary>
public static class AdaptiveDecision
{
    /// <summary>
    /// How many times to teach a concept again before moving on anyway.
    ///
    /// There is a limit to how many times being told the same thing differently helps, and past
    /// it the student is being held rather than taught. Better to move on, record that it was
    /// not mastered, and let an instructor see it than to trap someone in a loop they cannot
    /// leave.
    /// </summary>
    public const int MaxReteaches = 2;

    public static AdaptiveOutcome Decide(MasteryRecord record, AnswerJudgement judgement)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(judgement);

        if (!judgement.CarriesEvidence)
            return new AdaptiveOutcome(Adaptation.Continue, "Nothing was answered, so nothing was learned about them.");

        if (judgement.Verdict == Verdict.Incorrect)
        {
            return record.Reteaches >= MaxReteaches
                ? new AdaptiveOutcome(
                    Adaptation.MoveOnUnmastered,
                    $"Retaught {record.Reteaches} times without it landing. Moving on and flagging it.")
                : new AdaptiveOutcome(Adaptation.Reteach, "Wrong answer — come at it from a different angle.");
        }

        if (record.IsMastered)
            return new AdaptiveOutcome(Adaptation.Advance, $"Belief is {record.Belief:P0}; that is enough to move on.");

        return judgement.Verdict == Verdict.Partial
            ? new AdaptiveOutcome(Adaptation.Continue, "Partly there — the gap is what is left to teach.")
            : new AdaptiveOutcome(Adaptation.Continue, $"Right, but belief is only {record.Belief:P0}. One more.");
    }

    /// <summary>
    /// Whether a concept can be skipped outright.
    ///
    /// The other half of adapting, and the one students notice: being taught something they
    /// already demonstrated is the fastest way to lose them. Only ever on evidence — a record
    /// that was actually answered into, never a cold prior.
    /// </summary>
    public static bool ShouldSkip(MasteryRecord? record) =>
        record is not null && record.Evidence.Count > 0 && record.IsMastered;
}
