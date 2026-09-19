using Lumen.Domain.Common;

namespace Lumen.Domain.Assessment;

/// <param name="Verdict">What the answer was worth.</param>
/// <param name="BeliefAfter">Where the estimate landed, so the trail explains itself.</param>
public sealed record MasteryEvidence(
    DateTimeOffset At,
    Verdict Verdict,
    string Question,
    string Answer,
    string? Misconception,
    double BeliefAfter);

/// <summary>
/// What we believe a student knows about one concept, and why.
///
/// The evidence trail is not an audit afterthought. A mastery estimate is a number that decides
/// whether someone is taught something again, and a number nobody can interrogate is one nobody
/// should be allowed to act on — not the student who wants to know why they are being retaught,
/// and not the instructor looking at a cohort that all fell over in the same place.
/// </summary>
public sealed class MasteryRecord : Entity
{
    public Guid StudentId { get; set; }
    public Guid CourseId { get; set; }

    /// <summary>Stable across reprocessing, which is the whole point of it.</summary>
    public string ConceptKey { get; set; } = string.Empty;

    public string ConceptTitle { get; set; } = string.Empty;

    /// <summary>Belief the student knows this, 0 to 1.</summary>
    public double Belief { get; set; } = MasteryParameters.Default.Prior;

    public List<MasteryEvidence> Evidence { get; set; } = [];

    /// <summary>Times this concept has been taught again after a wrong answer.</summary>
    public int Reteaches { get; set; }

    /// <summary>
    /// The lesson gave up on this concept and carried on without it.
    ///
    /// Recorded rather than hidden. A student moved past something they never got is the single
    /// most useful thing an instructor can be shown, and the belief number alone does not say
    /// it — a low belief looks the same whether the concept was abandoned or simply not
    /// reached yet.
    /// </summary>
    public bool MovedOnUnmastered { get; set; }

    public bool IsMastered => BayesianMastery.IsMastered(Belief);

    public void Record(AnswerJudgement judgement, string question, string answer)
    {
        ArgumentNullException.ThrowIfNull(judgement);

        Belief = BayesianMastery.Observe(Belief, judgement.Verdict);
        UpdatedAt = DateTimeOffset.UtcNow;

        // Silence carries no information, so it leaves no evidence either — a trail full of
        // non-answers reads as a struggling student when nothing was actually observed.
        if (!judgement.CarriesEvidence) return;

        Evidence.Add(new MasteryEvidence(
            UpdatedAt, judgement.Verdict, question, answer, judgement.Misconception, Belief));
    }
}
