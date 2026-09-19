using Lumen.Domain.Common;
using Lumen.Domain.Teaching;

namespace Lumen.Domain.Scripts;

public enum ScriptNodeKind
{
    Speech,
    Visual,
    CheckForUnderstanding,
    Simulation,
    CodePlayground
}

/// <summary>
/// One beat of a lesson: what the tutor says, what is on the canvas while it says it, and
/// where an interjection may attach.
///
/// Nodes are deliberately short — fifteen to thirty seconds of speech — because the node is
/// the resolution of the resume pointer. Resuming to the wrong node in a lesson of eighty
/// nodes drops the student half a minute off, which is recoverable; at three minutes a node,
/// every wrong resume would be visibly wrong and no amount of retrieval quality would fix it.
/// </summary>
public sealed class ScriptNode : Entity
{
    public Guid LessonId { get; set; }

    /// <summary>Position within the lesson. Nodes are ordered by this, not by insertion.</summary>
    public int Ordinal { get; set; }

    public ScriptNodeKind Kind { get; set; }

    /// <summary>The concept this beat teaches, so mastery evidence can point somewhere real.</summary>
    public string ConceptKey { get; set; } = string.Empty;

    /// <summary>What is spoken. Also what the resume offset indexes into.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// The prosody layer. What makes speech sound taught rather than read is mostly here —
    /// slowing on the key clause, the beat before a reveal — and almost none of it survives
    /// being inferred from plain text at synthesis time.
    /// </summary>
    public string? Ssml { get; set; }

    /// <summary>Silence after this node. A semantic pause, not a rendering artefact.</summary>
    public int PauseAfterMs { get; set; }

    /// <summary>What the canvas shows during this node: a diagram, a code sample, a simulation.</summary>
    public string? VisualRef { get; set; }

    /// <summary>
    /// How the canvas moves while this node is spoken. Animated against the utterance offset,
    /// so an interrupted node resumes its illustration at the same frame rather than restarting.
    /// </summary>
    public VisualKind VisualKind { get; set; } = VisualKind.None;

    /// <summary>Read according to <see cref="VisualKind"/>. See <see cref="VisualSpec"/>.</summary>
    public string? VisualPayload { get; set; }

    /// <summary>Where in the source document this came from. Generated content stays traceable.</summary>
    public string? SourceRef { get; set; }

    /// <summary>
    /// True when this node states a technical term precisely. The register policy refuses to
    /// put an interjection inside one — the student will be examined on these words.
    /// </summary>
    public bool CarriesDefinition { get; set; }

    /// <summary>Where an interjection may attach, and what it would be doing there.</summary>
    public List<InterjectionSlot> InterjectionSlots { get; set; } = [];

    /// <summary>Pre-synthesised audio for <see cref="Text"/>, rendered once at publish time.</summary>
    public string? AudioKey { get; set; }

    public bool IsAssessment => Kind == ScriptNodeKind.CheckForUnderstanding;
}
