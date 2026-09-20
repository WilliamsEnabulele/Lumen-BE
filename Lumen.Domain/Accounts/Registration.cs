namespace Lumen.Domain.Accounts;

/// <summary>Why a sign-up or a sign-in was refused, in a form the endpoint can turn into words.</summary>
public enum AccountRefusal
{
    None = 0,
    BadEmail,
    WeakPassword,
    AlreadyRegistered,
    WrongEmailOrPassword
}

/// <summary>
/// The rules about accounts that are worth testing away from a request.
///
/// Sign-in deliberately has one refusal for two different failures. Telling somebody that an
/// address exists but the password is wrong hands an attacker a list of real accounts, and
/// hands anybody else a way to find out where a person has signed up.
/// </summary>
public static class Registration
{
    public static AccountRefusal CheckSignUp(string? email, string? password, bool alreadyTaken)
    {
        if (!Student.LooksLikeEmail(email)) return AccountRefusal.BadEmail;
        if (password is null || password.Length < PasswordHash.MinimumLength) return AccountRefusal.WeakPassword;
        if (alreadyTaken) return AccountRefusal.AlreadyRegistered;

        return AccountRefusal.None;
    }

    /// <summary>What the student is told. Written for a person, and never more than they need.</summary>
    public static string Explain(AccountRefusal refusal) => refusal switch
    {
        AccountRefusal.BadEmail => "That does not look like an email address.",
        AccountRefusal.WeakPassword =>
            $"A password needs at least {PasswordHash.MinimumLength} characters. Length beats punctuation.",
        AccountRefusal.AlreadyRegistered => "There is already an account with that address. Sign in instead.",
        AccountRefusal.WrongEmailOrPassword => "That email and password do not match an account.",
        _ => "That did not work.",
    };
}
