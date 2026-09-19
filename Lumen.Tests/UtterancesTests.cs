using Lumen.Domain.Ingestion;

namespace Lumen.Tests;

public class UtterancesTests
{
    [Fact]
    public void Splits_on_sentence_ends()
    {
        var sentences = Utterances.Sentences("A loop is a promise. The question is how often. That is all.");

        Assert.Equal(3, sentences.Count);
        Assert.Equal("A loop is a promise.", sentences[0]);
    }

    [Theory]
    [InlineData("Use a loop, e.g. a for loop, to repeat work.")]
    [InlineData("See Fig. 4 for the nested case.")]
    public void An_abbreviation_is_not_a_sentence_end(string text)
    {
        Assert.Single(Utterances.Sentences(text));
    }

    [Fact]
    public void Groups_sentences_up_to_the_target_length()
    {
        var text = string.Join(' ', Enumerable.Repeat("This sentence carries exactly seven words here.", 20));

        var utterances = Utterances.Split(text);

        Assert.True(utterances.Count > 1);
        Assert.All(utterances, utterance => Assert.True(Utterances.WordCount(utterance) <= Utterances.MaxWords));
    }

    [Fact]
    public void A_single_sentence_longer_than_the_ceiling_still_travels_whole()
    {
        // Cutting it would produce something no tutor could say. A coarse node is the lesser evil.
        var giant = string.Join(' ', Enumerable.Repeat("word", 200)) + ".";

        var utterances = Utterances.Split(giant);

        Assert.Single(utterances);
    }

    [Fact]
    public void Nothing_in_produces_nothing_out()
    {
        Assert.Empty(Utterances.Split("   "));
        Assert.Empty(Utterances.Split(string.Empty));
    }

    [Fact]
    public void Every_word_survives_the_split()
    {
        var text = "One two three. Four five six. Seven eight nine. Ten eleven twelve.";

        var rejoined = string.Join(' ', Utterances.Split(text, targetWords: 6, maxWords: 9));

        Assert.Equal(Utterances.WordCount(text), Utterances.WordCount(rejoined));
    }
}
