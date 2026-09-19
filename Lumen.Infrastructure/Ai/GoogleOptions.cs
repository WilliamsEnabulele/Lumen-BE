namespace Lumen.Infrastructure.Ai;

/// <summary>Which provider serves one of the three AI roles.</summary>
public enum AiProvider
{
    /// <summary>The deterministic fallback. Runs with no key and does not teach.</summary>
    None = 0,
    Anthropic = 1,
    Google = 2
}

/// <summary>
/// Which provider answers for each role, set independently.
///
/// Independently because the three roles are not the same purchase. Reading a document is one
/// large call per upload, paid once, and its output is validated by code before anybody sees
/// it — so a cheaper model is checked rather than trusted. Teaching is the recurring cost and
/// the product itself: it is spoken to the student directly, and nothing downstream catches a
/// dull explanation. Marking sits between the two.
///
/// One switch for all three would force the same answer to three different questions.
/// </summary>
public sealed class AiRouting
{
    public const string Section = "Ai:Routing";

    public AiProvider Author { get; set; } = AiProvider.Anthropic;
    public AiProvider Tutor { get; set; } = AiProvider.Anthropic;
    public AiProvider Judge { get; set; } = AiProvider.Anthropic;
}

public sealed class GoogleOptions
{
    public const string Section = "Ai:Google";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Model ids are configuration, never constants — the same rule the Anthropic side follows,
    /// and more load-bearing here: Google retires and renames Flash models on a shorter cycle
    /// than a deploy schedule.
    /// </summary>
    public string AuthorModel { get; set; } = "gemini-3.1-flash-lite";

    public int AuthorMaxTokens { get; set; } = 16000;

    /// <summary>
    /// The Developer API rather than Vertex, because it authenticates with an API key. Vertex
    /// wants a service account and a project, which is a deployment decision and not one to
    /// bury in a default.
    /// </summary>
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
