using Lumen.Domain.Scripts;
using Lumen.Domain.Sessions;

namespace Lumen.Tests;

public class ResumePointerTests
{
    private static readonly Guid Lesson = Guid.Parse("0197b9c2-0000-7000-8000-00000000000a");

    private static ScriptNode NodeWithCode() => new()
    {
        LessonId = Lesson,
        Ordinal = 7,
        Kind = ScriptNodeKind.CodePlayground,
        Text = "This is that same shape, wearing a business suit.",
        VisualRef = "loops-nested"
    };

    [Fact]
    public void A_fresh_node_starts_at_the_beginning_of_its_utterance()
    {
        var pointer = ResumePointer.AtStartOf(Lesson, NodeWithCode());

        Assert.Equal(0, pointer.UtteranceOffset);
        Assert.False(pointer.IsMidUtterance);
    }

    [Fact]
    public void The_pointer_carries_the_canvas_not_just_the_speech()
    {
        // Restoring the words but not the code panel is still a wrong resume.
        var pointer = ResumePointer.AtStartOf(Lesson, NodeWithCode());

        Assert.Equal("codeplayground:loops-nested", pointer.CanvasState);
    }

    [Fact]
    public void A_node_with_nothing_on_screen_says_so_explicitly()
    {
        var node = new ScriptNode { LessonId = Lesson, Kind = ScriptNodeKind.Speech, Text = "..." };

        Assert.Equal(ResumePointer.NoCanvas, ResumePointer.CanvasStateOf(node));
    }

    [Fact]
    public void Being_cut_off_records_exactly_where()
    {
        var pointer = ResumePointer.AtStartOf(Lesson, NodeWithCode()).HeldAt(31);

        Assert.Equal(31, pointer.UtteranceOffset);
        Assert.True(pointer.IsMidUtterance);
    }

    [Fact]
    public void A_negative_offset_is_not_a_position_in_an_utterance()
    {
        var pointer = ResumePointer.AtStartOf(Lesson, NodeWithCode());

        Assert.Throws<ArgumentOutOfRangeException>(() => pointer.HeldAt(-1));
    }

    [Fact]
    public void Two_pointers_at_the_same_place_are_the_same_pointer()
    {
        var node = NodeWithCode();

        Assert.Equal(ResumePointer.AtStartOf(Lesson, node), ResumePointer.AtStartOf(Lesson, node));
    }
}
