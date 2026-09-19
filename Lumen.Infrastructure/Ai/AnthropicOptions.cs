namespace Lumen.Infrastructure.Ai;

public sealed class AnthropicOptions
{
    public const string Section = "Ai";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Model ids are configuration, never constants. A model retired on notice should be a
    /// settings change, not a deploy.
    /// </summary>
    public string AuthorModel { get; set; } = "claude-opus-5";

    public string TutorModel { get; set; } = "claude-opus-5";

    /// <summary>A turn is a few sentences. Anything larger means the prompt is not being followed.</summary>
    public int TutorMaxTokens { get; set; } = 1500;

    public int AuthorMaxTokens { get; set; } = 16000;

    /// <summary>
    /// Marking one spoken answer is a small, well-bounded judgement — the cheapest call in the
    /// system and a reasonable place for a smaller model once there is evidence it agrees with
    /// the larger one.
    /// </summary>
    public string JudgeModel { get; set; } = "claude-opus-5";

    public int JudgeMaxTokens { get; set; } = 500;

    /// <summary>
    /// Inference leaves the country for most deployments, so every document sent is a
    /// cross-border transfer. Readiness refuses to report healthy until this is true.
    /// </summary>
    public bool ProcessorAgreementInPlace { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
