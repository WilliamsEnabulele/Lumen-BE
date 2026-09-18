using Lumen.Domain.Scripts;

namespace Lumen.Tests;

public class LessonScriptTests
{
    private static readonly Guid Lesson = Guid.Parse("0197b9c2-0000-7000-8000-00000000000a");

    private static ScriptNode Node(int ordinal) => new()
    {
        LessonId = Lesson,
        Ordinal = ordinal,
        Kind = ScriptNodeKind.Speech,
        Text = $"Node {ordinal}."
    };

    [Fact]
    public void Nodes_are_taught_in_ordinal_order_however_they_arrived()
    {
        var third = Node(3);
        var first = Node(1);
        var second = Node(2);

        var script = new LessonScript(Lesson, [third, first, second]);

        Assert.Equal(new[] { first.Id, second.Id, third.Id }, script.Nodes.Select(node => node.Id).ToArray());
        Assert.Equal(first.Id, script.First.Id);
    }

    [Fact]
    public void The_last_node_has_no_next()
    {
        var first = Node(1);
        var last = Node(2);
        var script = new LessonScript(Lesson, [first, last]);

        Assert.Equal(last.Id, script.Next(first.Id)?.Id);
        Assert.Null(script.Next(last.Id));
        Assert.True(script.IsLast(last.Id));
        Assert.False(script.IsLast(first.Id));
    }

    [Fact]
    public void A_pointer_at_an_unknown_node_finds_nothing_rather_than_guessing()
    {
        var script = new LessonScript(Lesson, [Node(1)]);

        Assert.Null(script.Find(Guid.CreateVersion7()));
        Assert.Null(script.Next(Guid.CreateVersion7()));
    }

    [Fact]
    public void A_script_with_no_nodes_cannot_be_taught()
    {
        Assert.Throws<ArgumentException>(() => new LessonScript(Lesson, Array.Empty<ScriptNode>()));
    }

    [Fact]
    public void A_duplicated_node_would_make_the_pointer_ambiguous()
    {
        var node = Node(1);

        Assert.Throws<ArgumentException>(() => new LessonScript(Lesson, [node, node]));
    }
}
