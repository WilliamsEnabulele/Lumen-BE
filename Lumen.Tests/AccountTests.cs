using Lumen.Domain.Accounts;

namespace Lumen.Tests;

/// <summary>
/// Storing a password. Every test here is a property that, if it stopped holding, would not
/// look like anything at all until a database went missing.
/// </summary>
public class PasswordHashTests
{
    private const string Password = "a-long-enough-password";

    [Fact]
    public void A_password_verifies_against_its_own_hash()
    {
        Assert.True(PasswordHash.Matches(Password, PasswordHash.Of(Password)));
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // A shared salt means one rainbow table breaks every account at once, and two people
        // who picked the same password can see it in the database.
        Assert.NotEqual(PasswordHash.Of(Password), PasswordHash.Of(Password));
    }

    [Fact]
    public void The_password_itself_is_nowhere_in_the_hash()
    {
        Assert.DoesNotContain(Password, PasswordHash.Of(Password), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("a-long-enough-passwore")]
    [InlineData("A-Long-Enough-Password")]
    [InlineData("")]
    [InlineData(" a-long-enough-password")]
    public void Anything_else_does_not(string attempt)
    {
        Assert.False(PasswordHash.Matches(attempt, PasswordHash.Of(Password)));
    }

    [Fact]
    public void The_cost_travels_with_the_hash_so_it_can_be_raised_later()
    {
        Assert.Contains($"${PasswordHash.Iterations}$", PasswordHash.Of(Password), StringComparison.Ordinal);
    }

    [Fact]
    public void A_hash_made_at_an_older_cost_still_verifies_and_is_flagged_for_rehashing()
    {
        // How a raised iteration count reaches somebody who never changes their password.
        var old = PasswordHash.Of(Password).Replace($"${PasswordHash.Iterations}$", "$100000$", StringComparison.Ordinal);

        Assert.True(PasswordHash.NeedsRehash(old));
        Assert.False(PasswordHash.NeedsRehash(PasswordHash.Of(Password)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$210000$notbase64$notbase64")]
    [InlineData("pbkdf2-sha256$1$c2FsdA==$aGFzaA==")]
    public void A_stored_value_that_is_not_a_hash_verifies_nothing(string? stored)
    {
        // Including a deliberately trivial iteration count: a hash claiming to have cost
        // nothing is a tampered hash, not a cheap one.
        Assert.False(PasswordHash.Matches(Password, stored));
    }

    [Fact]
    public void The_cost_is_high_enough_to_be_worth_having()
    {
        Assert.True(PasswordHash.Iterations >= 100_000, "PBKDF2 below this is not slowing anybody down");
    }
}

public class StudentTests
{
    [Theory]
    [InlineData("Ada@Example.COM", "ada@example.com")]
    [InlineData("  ada@example.com  ", "ada@example.com")]
    public void An_address_is_the_same_address_however_it_was_typed(string typed, string expected)
    {
        // Otherwise two casings are two accounts, and somebody pays twice and sees neither.
        Assert.Equal(expected, Student.KeyFor(typed));
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("ada.okonkwo+lumen@students.unilag.edu.ng")]
    public void Real_addresses_are_accepted(string email)
    {
        Assert.True(Student.LooksLikeEmail(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ada")]
    [InlineData("ada@")]
    [InlineData("@example.com")]
    [InlineData("ada@example")]
    [InlineData("ada@@example.com")]
    [InlineData("ada @example.com")]
    [InlineData("ada@.com")]
    public void Things_that_are_not_addresses_are_refused(string? email)
    {
        Assert.False(Student.LooksLikeEmail(email));
    }
}

public class AuthSessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-20T10:00:00Z");

    [Fact]
    public void A_session_recognises_the_token_it_issued()
    {
        var (session, token) = AuthSession.Issue(Guid.CreateVersion7(), Now);

        Assert.Equal(session.TokenHash, AuthSession.HashOf(token));
        Assert.True(session.IsUsableAt(Now));
    }

    [Fact]
    public void The_token_itself_is_never_what_is_stored()
    {
        // So that whoever reads this store cannot come away able to sign in as anybody.
        var (session, token) = AuthSession.Issue(Guid.CreateVersion7(), Now);

        Assert.NotEqual(token, session.TokenHash);
        Assert.DoesNotContain(token, session.TokenHash, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_sessions_never_share_a_token()
    {
        var tokens = Enumerable.Range(0, 200)
            .Select(_ => AuthSession.Issue(Guid.CreateVersion7(), Now).Token)
            .ToArray();

        Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, token => Assert.True(token.Length >= 40, $"'{token}' is not enough entropy"));
    }

    [Fact]
    public void A_token_survives_a_cookie_without_being_re_encoded()
    {
        // Base64url: nothing in it needs escaping in a cookie, a header or a URL.
        var (_, token) = AuthSession.Issue(Guid.CreateVersion7(), Now);

        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Fact]
    public void An_expired_session_is_no_longer_usable()
    {
        var (session, _) = AuthSession.Issue(Guid.CreateVersion7(), Now);

        Assert.True(session.IsUsableAt(Now + AuthSession.Lifetime - TimeSpan.FromMinutes(1)));
        Assert.False(session.IsUsableAt(Now + AuthSession.Lifetime));
    }

    [Fact]
    public void A_revoked_session_stops_working_at_once_rather_than_at_expiry()
    {
        // The whole reason the token is held server-side. Signing out has to mean now.
        var (session, _) = AuthSession.Issue(Guid.CreateVersion7(), Now);
        session.RevokedAt = Now;

        Assert.False(session.IsUsableAt(Now));
    }
}

public class RegistrationTests
{
    [Fact]
    public void A_good_sign_up_is_accepted()
    {
        Assert.Equal(
            AccountRefusal.None,
            Registration.CheckSignUp("ada@example.com", "a-long-enough-password", alreadyTaken: false));
    }

    [Fact]
    public void A_short_password_is_refused_and_told_why()
    {
        var refusal = Registration.CheckSignUp("ada@example.com", "short", alreadyTaken: false);

        Assert.Equal(AccountRefusal.WeakPassword, refusal);
        Assert.Contains($"{PasswordHash.MinimumLength}", Registration.Explain(refusal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_bad_address_is_caught_before_the_password_is_considered()
    {
        Assert.Equal(AccountRefusal.BadEmail, Registration.CheckSignUp("ada", "x", alreadyTaken: false));
    }

    [Fact]
    public void An_address_already_in_use_is_told_to_sign_in()
    {
        var refusal = Registration.CheckSignUp("ada@example.com", "a-long-enough-password", alreadyTaken: true);

        Assert.Equal(AccountRefusal.AlreadyRegistered, refusal);
        Assert.Contains("Sign in", Registration.Explain(refusal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_sign_in_never_says_which_half_was_wrong()
    {
        // Saying "no such account" hands an attacker a list of real ones, and hands anybody
        // else a way to find out where a person has signed up.
        var said = Registration.Explain(AccountRefusal.WrongEmailOrPassword);

        Assert.DoesNotContain("password is", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no account", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not found", said, StringComparison.OrdinalIgnoreCase);
    }
}
