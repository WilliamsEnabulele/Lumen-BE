using System.Security.Cryptography;
using System.Text;

namespace Lumen.Domain.Accounts;

/// <summary>
/// Turning a password into something safe to store, and checking one against it.
///
/// PBKDF2-HMAC-SHA256, because it is in the base class library and a password hash that needs
/// a package is one somebody eventually skips. Argon2 is better and is worth taking when there
/// is a reason to add a dependency; PBKDF2 at this iteration count is not the weak link in
/// anything here.
///
/// Three properties are load-bearing and each has a test: the same password hashes differently
/// every time, because a shared salt means one rainbow table breaks every account at once; the
/// cost is recorded inside the hash, so it can be raised later without invalidating everybody;
/// and the comparison is constant time, because one that returns early tells an attacker how
/// much of a guess was right.
/// </summary>
public static class PasswordHash
{
    /// <summary>
    /// Deliberately slow. This is the whole defence once a database is stolen: the number is
    /// chosen so a single check costs a fraction of a second here and makes guessing a hundred
    /// million passwords cost somebody years.
    /// </summary>
    public const int Iterations = 210_000;

    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    /// <summary>
    /// Short enough to be typed on a phone, long enough to be worth the arithmetic above. A
    /// length floor rather than a character-class rule, because forcing a symbol produces
    /// "Password1!" and nothing else.
    /// </summary>
    public const int MinimumLength = 10;

    public static string Of(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);

        // The cost travels with the hash so it can be raised without locking anybody out.
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Matches(string password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations < 1000) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Whether a hash was made with today's cost. A password checked against an older one is
    /// worth re-hashing on the way past, which is how a raised cost reaches people who never
    /// change their password.
    /// </summary>
    public static bool NeedsRehash(string? stored) =>
        stored is null
        || !stored.StartsWith($"pbkdf2-sha256${Iterations}$", StringComparison.Ordinal);
}
