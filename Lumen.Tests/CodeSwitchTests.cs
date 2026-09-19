using Lumen.Domain.Teaching;

namespace Lumen.Tests;

/// <summary>
/// The detector that decides whether a student invited Pidgin.
///
/// Both directions of failure are tested, but they are not equally bad and the tests say so.
/// Missing a code-switch leaves the tutor in standard English, which is never an insult.
/// Inventing one has a machine start performing somebody's register at them uninvited, which
/// is the thing the whole register design exists to prevent.
/// </summary>
public class CodeSwitchTests
{
    [Theory]
    [InlineData("abeg explain am again")]
    [InlineData("na so I been think")]
    [InlineData("wetin be the difference?")]
    [InlineData("I no sabi this one at all")]
    [InlineData("e be like say the loop no dey stop")]
    [InlineData("Oya, next one")]
    [InlineData("no wahala, I get am")]
    public void Pidgin_in_the_answer_is_a_code_switch(string said)
    {
        Assert.True(CodeSwitch.IsCodeSwitch(said), $"'{said}' should have been heard as Pidgin");
    }

    [Theory]
    [InlineData("It runs five times, zero through four.")]
    [InlineData("Because the condition never becomes false.")]
    [InlineData("They are indexed from zero, so the last one is four.")]
    [InlineData("I think the array has five elements in it.")]
    [InlineData("Sorry, could you say that again?")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_English_is_never_mistaken_for_it(string? said)
    {
        Assert.False(CodeSwitch.IsCodeSwitch(said), $"'{said}' should not have been heard as Pidgin");
    }

    [Fact]
    public void Markers_are_matched_on_whole_words()
    {
        // The failure a Contains check makes: "dey" inside "they", "abi" inside "ability".
        Assert.False(CodeSwitch.IsCodeSwitch("They said the ability to break out early matters."));
    }

    [Fact]
    public void What_was_matched_can_be_read_back()
    {
        Assert.Equal(new[] { "abeg", "wetin" }, CodeSwitch.MarkersIn("Abeg, wetin be that?"));
    }

    [Theory]
    [InlineData("please speak english")]
    [InlineData("can you talk properly")]
    [InlineData("no pidgin please")]
    [InlineData("just plain english thanks")]
    public void Asking_for_plain_English_is_recognised(string said)
    {
        Assert.True(CodeSwitch.AsksForPlainEnglish(said));
    }
}

public class RegisterTrackingTests
{
    private static TeachingSession Hearing(params string?[] utterances)
    {
        var session = new TeachingSession();
        foreach (var said in utterances) RegisterTracking.Hear(session, said);
        return session;
    }

    [Fact]
    public void The_tutor_opens_in_standard_English_at_everybody()
    {
        Assert.Equal(RegisterLevel.StandardEnglish, new TeachingSession().Register);
    }

    [Fact]
    public void One_Pidgin_word_is_not_an_invitation()
    {
        Assert.Equal(RegisterLevel.StandardEnglish, Hearing("abeg, say that again").Register);
    }

    [Fact]
    public void Two_in_a_row_moves_the_tutor_one_step_toward_them()
    {
        Assert.Equal(RegisterLevel.LightInterjection, Hearing("abeg say am again", "I no sabi").Register);
    }

    [Fact]
    public void Each_step_costs_its_own_evidence()
    {
        // Two utterances buys one rung, not the whole ladder.
        var session = Hearing("abeg", "wetin be this");
        Assert.Equal(RegisterLevel.LightInterjection, session.Register);

        RegisterTracking.Hear(session, "oya continue");
        Assert.Equal(RegisterLevel.LightInterjection, session.Register);

        RegisterTracking.Hear(session, "e dey work now");
        Assert.Equal(RegisterLevel.ComfortableCodeSwitch, session.Register);
    }

    [Fact]
    public void Standard_English_in_between_breaks_the_run()
    {
        Assert.Equal(
            RegisterLevel.StandardEnglish,
            Hearing("abeg", "It runs five times.", "wetin be that").Register);
    }

    [Fact]
    public void Silence_neither_builds_nor_breaks_the_run()
    {
        // A student thinking between answers has not stopped code-switching.
        Assert.Equal(RegisterLevel.LightInterjection, Hearing("abeg", null, "wetin be that").Register);
    }

    [Fact]
    public void Asking_for_plain_English_is_answered_at_once()
    {
        var session = Hearing("abeg", "wetin be this");
        Assert.Equal(RegisterLevel.LightInterjection, session.Register);

        RegisterTracking.Hear(session, "can you speak english please");

        Assert.Equal(RegisterLevel.StandardEnglish, session.Register);
    }

    [Fact]
    public void And_is_never_re_opened_by_a_later_slip()
    {
        // The one that matters. A student who has asked to be spoken to plainly and then uses
        // a Pidgin word has not changed their mind, and treating it as permission would be the
        // machine waiting for an excuse.
        var session = Hearing("speak english please", "abeg", "wetin be that", "oya");

        Assert.Equal(RegisterLevel.StandardEnglish, session.Register);
        Assert.True(session.PlainEnglishRequested);
    }
}
