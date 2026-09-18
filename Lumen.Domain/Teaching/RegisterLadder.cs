namespace Lumen.Domain.Teaching;

/// <summary>
/// How a student's register level moves.
///
/// One direction only, and only on evidence: <b>the student's register pulls the tutor's,
/// never the other way round.</b> That is what a human lecturer does — they hear how you
/// speak and meet you there — and it means the system never has to guess on a student's
/// behalf. Guessing is the failure: a tutor that opens in Pidgin at a student who did not
/// invite it has made an assumption about them from nothing.
///
/// Rising takes repeated evidence; falling takes one signal. That asymmetry is deliberate.
/// Reading one stray Pidgin word as an invitation is exactly the over-eager behaviour this
/// is here to prevent, while a student who asks to be spoken to plainly is owed that
/// immediately.
/// </summary>
public static class RegisterLadder
{
    /// <summary>Consecutive code-switches by the student before the tutor moves up a level.</summary>
    public const int EvidenceToRise = 2;

    public static RegisterLevel Observe(RegisterLevel current, int consecutiveStudentCodeSwitches)
    {
        if (consecutiveStudentCodeSwitches < EvidenceToRise) return current;

        return current switch
        {
            RegisterLevel.StandardEnglish => RegisterLevel.LightInterjection,
            RegisterLevel.LightInterjection => RegisterLevel.ComfortableCodeSwitch,
            _ => current
        };
    }

    /// <summary>A student asking for plain English gets it at once, and from the next utterance.</summary>
    public static RegisterLevel RequestedPlain() => RegisterLevel.StandardEnglish;
}
