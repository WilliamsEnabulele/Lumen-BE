namespace Lumen.Domain.Teaching;

/// <summary>
/// How far the tutor may move from standard English toward the student's own register.
///
/// This is a per-student setting, not a product style, because register preference is not
/// uniform: the same Naija Pidgin interjection that reads as warmth to one student reads as
/// a machine being unserious about their education to another. Defaulting to anything but
/// the conservative end is a decision made on the student's behalf that nobody asked for.
/// </summary>
public enum RegisterLevel
{
    /// <summary>Standard Nigerian English throughout. The default, and always available.</summary>
    StandardEnglish = 0,

    /// <summary>Occasional interjection at the edges of an explanation. Never inside one.</summary>
    LightInterjection = 1,

    /// <summary>Comfortable code-switching, still only in the affect, never in the content.</summary>
    ComfortableCodeSwitch = 2
}

/// <summary>
/// What an interjection is *doing*, which is the thing that decides where it may attach.
/// These are speech acts, not decoration: "Ah-ah!" softening a correction and "Na so!"
/// confirming a right answer are not interchangeable, and swapping them is the failure that
/// reads as a machine performing a register rather than speaking one.
/// </summary>
public enum InterjectionFunction
{
    Encouragement,
    Surprise,
    CorrectionSoftener,
    Emphasis,
    Transition,
    SharedKnowledge
}

public enum SlotPosition
{
    BeforeNode,
    AfterNode
}

/// <summary>
/// A position in a script where an interjection may go, and what it would be doing there.
/// A slot is a position, not a string: the actual interjection is chosen at runtime, because
/// an exclamation baked into the script at publish time fires whether or not the student did
/// the thing being reacted to, which is a laugh track.
/// </summary>
public sealed record InterjectionSlot(SlotPosition Position, InterjectionFunction Function);
