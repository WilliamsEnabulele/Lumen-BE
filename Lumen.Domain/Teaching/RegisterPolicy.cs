namespace Lumen.Domain.Teaching;

/// <summary>
/// Whether an interjection may be played here, now.
///
/// The governing rule is one line: <b>the interjection carries the affect, the explanation
/// carries the content.</b> Everything below follows from it. A student is going to be
/// examined in standard English, so the precise terms stay precise; what code-switching does
/// in a real classroom is solidarity, softening a correction, and marking surprise — work
/// that happens at the edges of an explanation, not inside it.
///
/// These are refusals rather than preferences because the failure mode is not "sounds a bit
/// off". A machine over-performing a register is a caricature, and a caricature of how
/// someone speaks is worse than plain English by a wide margin.
/// </summary>
public static class RegisterPolicy
{
    /// <summary>
    /// Floor on how often an interjection may land. Chosen to be conversational rather than
    /// constant: roughly one per substantial explanation, not one per breath.
    /// </summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(90);

    /// <param name="isAssessment">True when the tutor is asking the student something.</param>
    /// <param name="carriesDefinition">True when the words state a technical term precisely.</param>
    public static InterjectionDecision Decide(
        RegisterLevel level,
        bool isAssessment,
        bool carriesDefinition,
        Interjection interjection,
        TimeSpan sinceLastInterjection,
        bool alreadyCarriesOne)
    {
        ArgumentNullException.ThrowIfNull(interjection);

        if (level == RegisterLevel.StandardEnglish)
            return InterjectionDecision.No("This student is set to standard English.");

        if (interjection.MinimumLevel > level)
            return InterjectionDecision.No(
                $"{interjection.Function} needs register level {interjection.MinimumLevel}; this student is at {level}.");

        if (isAssessment)
            return InterjectionDecision.No(
                "Never during an assessment item. A formal question asked in an informal register changes what is being asked.");

        if (carriesDefinition)
            return InterjectionDecision.No(
                "Never inside a technical definition. The student is examined in standard English, so the terms stay precise.");

        if (alreadyCarriesOne)
            return InterjectionDecision.No("This turn already carries one. Two in a breath is a performance.");

        if (sinceLastInterjection < MinimumGap)
            return InterjectionDecision.No(
                $"Only {sinceLastInterjection.TotalSeconds:F0}s since the last one; the floor is {MinimumGap.TotalSeconds:F0}s.");

        if (!interjection.HasAudio)
            return InterjectionDecision.No(
                "No recorded take. A general-purpose voice reading this phonetically is worse than not saying it.");

        return InterjectionDecision.Yes;
    }
}

public sealed record InterjectionDecision(bool Allowed, string? Refusal)
{
    public static readonly InterjectionDecision Yes = new(true, null);

    public static InterjectionDecision No(string reason) => new(false, reason);
}
