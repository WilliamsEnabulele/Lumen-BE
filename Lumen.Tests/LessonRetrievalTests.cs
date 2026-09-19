using Lumen.Domain.Retrieval;
using Lumen.Domain.Scripts;

namespace Lumen.Tests;

/// <summary>
/// Two behaviours matter more than the ranking: an answer names where it came from, and a
/// question the lesson does not cover is refused rather than guessed at.
/// </summary>
public class LessonRetrievalTests
{
    private static ScriptNode Node(int ordinal, string text, ScriptNodeKind kind = ScriptNodeKind.Speech) => new()
    {
        Ordinal = ordinal,
        Kind = kind,
        Text = text,
        SourceRef = $"p{ordinal}",
    };

    private static readonly ScriptNode[] Lesson =
    [
        Node(1, "A loop is a promise to do the same work more than once."),
        Node(2, "Nesting puts one loop inside another, so the inner loop restarts on every outer turn."),
        Node(3, "The counts do not add together, they multiply, and multiplication gets away from you fast."),
        Node(4, "So what is nesting?", ScriptNodeKind.CheckForUnderstanding),
    ];

    [Fact]
    public void Answers_from_the_passage_that_covers_the_question()
    {
        var result = LessonRetrieval.Answer(Lesson, "what does nesting do to the inner loop?");

        Assert.True(result.InScope);
        Assert.Equal(Lesson[1].Id, result.SourceNodeId);
    }

    [Fact]
    public void Every_answer_says_where_it_came_from()
    {
        var result = LessonRetrieval.Answer(Lesson, "why does it multiply?");

        Assert.True(result.InScope);
        Assert.NotNull(result.SourceNodeId);
        Assert.False(string.IsNullOrWhiteSpace(result.SourceRef));
    }

    [Fact]
    public void A_question_the_lesson_does_not_cover_is_refused_not_guessed_at()
    {
        var result = LessonRetrieval.Answer(Lesson, "who won the league in 1994?");

        Assert.False(result.InScope);
        Assert.Null(result.SourceNodeId);
        Assert.Contains("outside this lesson", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_students_plural_still_finds_the_passage()
    {
        var result = LessonRetrieval.Answer(Lesson, "tell me about loops");

        Assert.True(result.InScope);
    }

    [Fact]
    public void A_check_question_is_never_offered_back_as_an_answer()
    {
        var result = LessonRetrieval.Answer(Lesson, "so what is nesting");

        Assert.NotEqual(Lesson[3].Id, result.SourceNodeId);
    }

    [Fact]
    public void Where_two_passages_fit_the_nearer_one_wins()
    {
        // Being asked "why does that multiply" while node 3 is on screen should not send the
        // student back to node 1 for a worse match.
        var result = LessonRetrieval.Answer(Lesson, "multiply", Lesson[2].Id);

        Assert.Equal(Lesson[2].Id, result.SourceNodeId);
    }

    [Fact]
    public void An_empty_question_is_not_an_answer()
    {
        Assert.False(LessonRetrieval.Answer(Lesson, "   ").InScope);
        Assert.False(LessonRetrieval.Answer([], "what is a loop?").InScope);
    }
}
