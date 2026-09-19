namespace Lumen.Infrastructure.Ai;

/// <summary>The three things a model is asked to do here.</summary>
public enum AiRole
{
    /// <summary>Read the document and decide what it teaches.</summary>
    Author,

    /// <summary>Teach a turn.</summary>
    Tutor,

    /// <summary>Mark one spoken answer.</summary>
    Judge
}

/// <summary>
/// Turns configuration into which implementation actually serves each role.
///
/// Pure, and separate from the container, because the failure this prevents is the quiet one.
/// A role routed to a provider that cannot serve it — no key, or no implementation written yet
/// — must not fall through to something else that happens to be registered. Somebody choosing
/// Gemini for cost and silently getting the deterministic fallback would see a bill they liked
/// and a product that stopped teaching, and nothing would say which had happened.
/// </summary>
public static class AiWiring
{
    /// <summary>
    /// Which roles each provider can actually serve today. Deliberately explicit: a provider
    /// half-implemented is worse than one not offered, because it is configurable.
    /// </summary>
    public static bool Supports(AiProvider provider, AiRole role) => provider switch
    {
        AiProvider.None => true,
        AiProvider.Anthropic => true,
        AiProvider.Google => role == AiRole.Author,
        _ => false,
    };

    /// <summary>
    /// The provider that will serve this role, or an exception saying why the configured one
    /// cannot. Falls back to the deterministic pair only when nobody asked for anything —
    /// running keyless is a development convenience, and never a substitute for a choice.
    /// </summary>
    public static AiProvider Resolve(
        AiRole role,
        AiProvider wanted,
        bool chosenExplicitly,
        bool anthropicReady,
        bool googleReady)
    {
        if (!Supports(wanted, role))
            throw new InvalidOperationException(
                $"{role} cannot be served by {wanted}: there is no {wanted} implementation for that role yet. "
                + $"Route {role} to Anthropic, or write one.");

        var ready = wanted switch
        {
            AiProvider.Anthropic => anthropicReady,
            AiProvider.Google => googleReady,
            _ => true,
        };

        if (ready) return wanted;

        if (chosenExplicitly)
            throw new InvalidOperationException(
                $"{role} is routed to {wanted}, but no {wanted} key is configured. "
                + $"Set {Key(wanted)}, or route {role} somewhere else.");

        // Nothing was asked for and nothing is configured: the keyless development mode.
        return AiProvider.None;
    }

    private static string Key(AiProvider provider) => provider switch
    {
        AiProvider.Anthropic => "Ai:ApiKey (or the ANTHROPIC_API_KEY environment variable)",
        AiProvider.Google => "Ai:Google:ApiKey (or the GOOGLE_API_KEY environment variable)",
        _ => "an API key",
    };
}
