using Lumen.Domain.Assessment;

namespace Lumen.Domain.Teaching;

/// <summary>
/// What happens at the moment a concept is entered, before a word of it is taught.
///
/// Separate from <see cref="TurnDirector"/> because it answers a different question. The
/// director decides what to do on this concept; this decides whether there is any point being
/// on it at all. Being taught something you already demonstrated is the fastest way to lose a
/// student, and it is also the most expensive thing the platform can do — a skipped concept is
/// a lesson's worth of generation nobody pays for.
/// </summary>
public static class ConceptEntry
{
    /// <summary>
    /// Advances the session past any concept the student has already demonstrated, and returns
    /// what was skipped so the tutor can say so rather than silently jumping.
    ///
    /// Only ever acts on entry — a concept already being taught is the director's business, and
    /// a mastery estimate that ticked over mid-explanation is not a reason to abandon the
    /// sentence. Only ever on evidence, too: <see cref="AdaptiveDecision.ShouldSkip"/> refuses
    /// a cold prior, so a student who has answered nothing is taught everything.
    /// </summary>
    public static IReadOnlyList<string> SkipKnown(
        TeachingSession session,
        LessonPlan plan,
        Func<PlannedConcept, MasteryRecord?> known)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(known);

        var skipped = new List<string>();

        // Mid-concept, nothing is skipped. A turn spent here means the explanation is underway.
        if (session.TurnsOnConcept > 0) return skipped;

        while (!session.Complete)
        {
            var concept = session.CurrentConcept(plan);
            if (concept is null) break;
            if (!AdaptiveDecision.ShouldSkip(known(concept))) break;

            skipped.Add(concept.Title);
            session.Advance(plan);
        }

        return skipped;
    }
}
