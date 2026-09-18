using Lumen.Domain.Sessions;

namespace Lumen.Tests;

/// <summary>
/// Resuming at the exact character the tutor was cut off on is precise and sounds wrong. The
/// pointer keeps that offset because it is the truth about what the student heard; this is
/// the separate judgement about where to start speaking again.
/// </summary>
public class UtteranceBoundaryTests
{
    [Fact]
    public void Resumes_from_the_start_of_the_sentence_it_was_cut_off_in()
    {
        const string text = "A loop is a promise. The interesting question is how many times it is kept.";
        var expected = text.IndexOf("The interesting", StringComparison.Ordinal);

        Assert.Equal(expected, UtteranceBoundary.SnapBack(text, text.IndexOf("how many", StringComparison.Ordinal)));
    }

    [Fact]
    public void Falls_back_to_the_start_of_the_clause_when_there_is_no_sentence_break()
    {
        const string text = "Ten outer turns, ten inner turns on each one";
        var expected = text.IndexOf("ten inner", StringComparison.Ordinal);

        Assert.Equal(expected, UtteranceBoundary.SnapBack(text, text.IndexOf("on each", StringComparison.Ordinal)));
    }

    [Fact]
    public void Never_resumes_half_way_through_a_word()
    {
        const string text = "hello world";

        Assert.Equal(text.IndexOf("world", StringComparison.Ordinal), UtteranceBoundary.SnapBack(text, 8));
    }

    [Fact]
    public void Will_not_rewind_further_than_the_limit()
    {
        var tail = string.Join(' ', Enumerable.Repeat("word", 60));
        var text = "Start of a long stretch. " + tail;

        var landed = UtteranceBoundary.SnapBack(text, text.Length);

        // The sentence break is well past the rewind limit, so it lands on a word instead.
        Assert.Equal(text.LastIndexOf("word", StringComparison.Ordinal), landed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Nothing_spoken_means_nothing_to_rewind(int offset)
    {
        Assert.Equal(0, UtteranceBoundary.SnapBack("A loop is a promise.", offset));
    }

    [Fact]
    public void An_offset_past_the_end_is_clamped_rather_than_thrown()
    {
        const string text = "A loop is a promise. The question is how often.";

        Assert.Equal(
            UtteranceBoundary.SnapBack(text, text.Length),
            UtteranceBoundary.SnapBack(text, text.Length + 500));
    }

    [Fact]
    public void An_empty_utterance_resumes_at_zero()
    {
        Assert.Equal(0, UtteranceBoundary.SnapBack("", 10));
    }

    [Fact]
    public void Never_returns_a_position_past_where_the_student_was_cut_off()
    {
        const string text = "One. Two. Three. Four.";

        for (var offset = 0; offset <= text.Length; offset++)
            Assert.True(UtteranceBoundary.SnapBack(text, offset) <= offset);
    }
}
