using System.Text.RegularExpressions;

namespace Lumen.Domain.Teaching;

/// <summary>
/// Whether a student just spoke Naija Pidgin, and whether they just asked not to be.
///
/// Deliberately a word list rather than a model call, for three reasons. It runs on every
/// utterance, so a second round trip per turn would be paid constantly for a signal worth
/// very little. It has to be inspectable — <see cref="MarkersIn"/> returns exactly what it
/// matched, so "why did the tutor start speaking Pidgin at me" has an answer that is not a
/// shrug. And it fails closed by construction: a list can only ever be too small, and a
/// detector that misses a code-switch keeps the tutor in standard English, which is the
/// direction that never insults anybody.
///
/// The list is therefore short and unambiguous on purpose. Markers that are also ordinary
/// English words, or common abbreviations, are left out even though they are real Pidgin —
/// a false positive here has the tutor start performing a register at a student who never
/// invited it, which is the exact failure the whole register design exists to prevent.
/// </summary>
public static class CodeSwitch
{
    private static readonly string[] Markers =
    [
        "abeg", "abi", "biko", "chai", "comot", "dey", "ehen", "jare", "nawa", "oga",
        "omo", "oya", "pikin", "sabi", "sef", "shey", "una", "wahala", "wetin",
        "na so", "no be", "no wahala", "no vex", "e be like", "e dey", "e go",
        "make i", "make we", "i no sabi", "you no", "as e dey",
    ];

    /// <summary>
    /// Asking to be spoken to plainly. Matched loosely on purpose — every false positive here
    /// lands on standard English, which is never the wrong thing to give someone.
    /// </summary>
    private static readonly string[] PlainRequests =
    [
        "speak english", "in english", "plain english", "english please", "proper english",
        "no pidgin", "stop the pidgin", "stop with the pidgin", "don't use pidgin",
        "dont use pidgin", "no more pidgin", "speak properly", "talk properly",
        "speak normally", "talk normally", "be serious",
    ];

    private static readonly Regex Word = new(@"[a-z']+", RegexOptions.Compiled);

    /// <summary>Every marker this utterance matched, so the decision can be read back.</summary>
    public static IReadOnlyList<string> MarkersIn(string? said)
    {
        if (string.IsNullOrWhiteSpace(said)) return [];

        // Normalised to bare words separated by single spaces, so a marker can be matched on
        // word boundaries without punctuation or casing getting in the way. " dey " will not
        // match inside "they", which a naive Contains would.
        var normalised = " " + string.Join(' ', Word.Matches(said.ToLowerInvariant()).Select(m => m.Value)) + " ";

        return Markers.Where(marker => normalised.Contains($" {marker} ", StringComparison.Ordinal)).ToArray();
    }

    public static bool IsCodeSwitch(string? said) => MarkersIn(said).Count > 0;

    public static bool AsksForPlainEnglish(string? said)
    {
        if (string.IsNullOrWhiteSpace(said)) return false;

        var normalised = string.Join(' ', Word.Matches(said.ToLowerInvariant()).Select(m => m.Value));
        return PlainRequests.Any(request => normalised.Contains(request, StringComparison.Ordinal));
    }
}

/// <summary>
/// Moves a session's register as the student speaks.
///
/// The rule the whole feature rests on: <b>the student's register pulls the tutor's, never the
/// other way round.</b> The tutor opens in standard English at everybody, and only ever meets
/// someone where they have already shown they are.
/// </summary>
public static class RegisterTracking
{
    /// <summary>
    /// Reads one student utterance and returns the register the tutor should now use.
    ///
    /// Silence is not a signal in either direction: a student who says nothing has neither
    /// code-switched nor stopped, so the run of evidence is left exactly where it was rather
    /// than reset. Resetting on silence would mean a student who thinks between answers could
    /// never build two consecutive anything.
    /// </summary>
    public static RegisterLevel Hear(TeachingSession session, string? said)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(said)) return session.Register;

        // Asked for plain English: given immediately, and for good. Nothing they say later
        // re-opens it — a student who has once said "speak properly" and then uses a Pidgin
        // word has not changed their mind, and taking it as permission would be the machine
        // waiting for an excuse.
        if (CodeSwitch.AsksForPlainEnglish(said))
        {
            session.PlainEnglishRequested = true;
            session.ConsecutiveCodeSwitches = 0;
            session.Register = RegisterLadder.RequestedPlain();
            return session.Register;
        }

        if (session.PlainEnglishRequested) return session.Register;

        session.ConsecutiveCodeSwitches = CodeSwitch.IsCodeSwitch(said)
            ? session.ConsecutiveCodeSwitches + 1
            : 0;

        var risen = RegisterLadder.Observe(session.Register, session.ConsecutiveCodeSwitches);

        // Each rung costs its own evidence. Without this reset the run keeps counting and the
        // ladder climbs on every further utterance, so two Pidgin words in a row would carry a
        // student all the way to the top rather than one step toward them.
        if (risen != session.Register) session.ConsecutiveCodeSwitches = 0;

        session.Register = risen;
        return session.Register;
    }
}
