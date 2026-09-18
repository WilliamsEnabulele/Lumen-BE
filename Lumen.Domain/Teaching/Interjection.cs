using Lumen.Domain.Common;

namespace Lumen.Domain.Teaching;

/// <summary>
/// One interjection, with the audio that says it.
///
/// The audio matters more than the text. Prosody is most of an interjection's meaning —
/// "Ah-ah" rising and "Ah-ah" falling are different speech acts — so these are not handed to
/// a general-purpose voice to read phonetically. Each carries its own clip, and a set of
/// takes, because the same clip played identically twice is what makes a tutor sound like a
/// machine faster than any accent ever will.
/// </summary>
public sealed class Interjection : Entity
{
    /// <summary>The written form, for the transcript. Never the thing that is synthesised.</summary>
    public string Text { get; set; } = string.Empty;

    public InterjectionFunction Function { get; set; }

    /// <summary>Below this level, this interjection is never selected.</summary>
    public RegisterLevel MinimumLevel { get; set; } = RegisterLevel.LightInterjection;

    public string Locale { get; set; } = "en-NG";

    /// <summary>
    /// Object-storage keys for the recorded takes. More than one so the same reaction does
    /// not arrive in identical packaging every time.
    /// </summary>
    public List<string> AudioKeys { get; set; } = [];

    public bool HasAudio => AudioKeys.Count > 0;
}
