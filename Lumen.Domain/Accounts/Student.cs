using Lumen.Domain.Common;

namespace Lumen.Domain.Accounts;

/// <summary>
/// Somebody who learns here.
///
/// The thing every other record in the system has been pretending to have. Courses, sessions,
/// mastery, payments and the free allowance were all filed against one hardcoded id, which
/// made them one shared pile — one person's free upload spent everybody's, and one person's
/// subscription entitled the world.
/// </summary>
public sealed class Student : Entity
{
    /// <summary>Normalised, and the only way in. Stored as the student typed it for display.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Lowercased and trimmed. What uniqueness is decided on, so two casings are one account.</summary>
    public string EmailKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public DateTimeOffset? LastSignedInAt { get; set; }

    /// <summary>
    /// An address is an address regardless of how it was typed, so casing and surrounding
    /// whitespace must not be able to create a second account for the same person — which is
    /// how somebody ends up paying twice and seeing neither subscription.
    /// </summary>
    public static string KeyFor(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Deliberately permissive: something, an @, something with a dot. Anything stricter
    /// rejects real addresses, and the only check that actually proves an address works is
    /// sending to it — which is a separate feature, not a regex.
    /// </summary>
    public static bool LooksLikeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        var trimmed = email.Trim();
        if (trimmed.Length is < 5 or > 254 || trimmed.Any(char.IsWhiteSpace)) return false;

        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1) return false;

        var domain = trimmed[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.');
    }
}
