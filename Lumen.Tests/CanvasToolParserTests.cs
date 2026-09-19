using System.Text.Json;
using Lumen.Domain.Canvas;

namespace Lumen.Tests;

/// <summary>
/// A schema can say `line` is an integer. It cannot say the code on the canvas has four lines,
/// so line nine is meaningless. Every case here is that shape: coherent JSON describing an
/// incoherent picture, caught before it reaches a student.
/// </summary>
public class CanvasToolParserTests
{
    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement;

    private static CanvasState WithCode(string source, int? highlight = null) =>
        CanvasState.Empty.Apply(new ShowCode("python", source, highlight));

    private const string ThreeLines = "for i in range(10):\n    for j in range(10):\n        print(i, j)";

    [Fact]
    public void A_tool_that_does_not_exist_is_named_rather_than_ignored()
    {
        var result = CanvasToolParser.Parse("show_hologram", Input("{}"), CanvasState.Empty);

        Assert.False(result.Drawn);
        Assert.Contains("show_hologram", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_statement_is_a_line_not_a_paragraph()
    {
        var tooLong = new string('x', CanvasTools.MaxStatementLength + 1);

        var result = CanvasToolParser.Parse(
            CanvasTools.ShowStatement,
            Input($$"""{"text":"{{tooLong}}"}"""),
            CanvasState.Empty);

        Assert.False(result.Drawn);
        Assert.Contains("show_steps", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_good_statement_goes_up()
    {
        var result = CanvasToolParser.Parse(
            CanvasTools.ShowStatement,
            Input("""{"text":"A nested loop multiplies, it does not add."}"""),
            CanvasState.Empty);

        var statement = Assert.IsType<ShowStatement>(result.Command);
        Assert.Equal("A nested loop multiplies, it does not add.", statement.Text);
    }

    [Fact]
    public void Highlighting_a_line_that_is_not_there_is_refused()
    {
        var result = CanvasToolParser.Parse(
            CanvasTools.HighlightCode,
            Input("""{"line":9}"""),
            WithCode(ThreeLines));

        Assert.False(result.Drawn);
        Assert.Contains("3 lines", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Highlighting_with_an_empty_canvas_says_to_show_the_code_first()
    {
        var result = CanvasToolParser.Parse(CanvasTools.HighlightCode, Input("""{"line":1}"""), CanvasState.Empty);

        Assert.False(result.Drawn);
        Assert.Contains("show_code", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Highlighting_moves_the_line_rather_than_replacing_the_code()
    {
        var canvas = WithCode(ThreeLines, highlight: 1);

        var result = CanvasToolParser.Parse(CanvasTools.HighlightCode, Input("""{"line":2}"""), canvas);
        var after = canvas.Apply(result.Command!);

        var code = Assert.IsType<ShowCode>(after.Current);
        Assert.Equal(2, code.HighlightLine);
        Assert.Equal(ThreeLines, code.Source);
    }

    [Fact]
    public void Code_with_a_highlight_beyond_its_own_length_is_refused()
    {
        var result = CanvasToolParser.Parse(
            CanvasTools.ShowCode,
            Input("""{"language":"python","source":"print(1)","highlight_line":4}"""),
            CanvasState.Empty);

        Assert.False(result.Drawn);
        Assert.Contains("1 lines", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void An_arrow_to_a_box_that_does_not_exist_is_refused()
    {
        var result = CanvasToolParser.Parse(
            CanvasTools.ShowDiagram,
            Input("""
            {"nodes":[{"id":"a","label":"Outer loop"},{"id":"b","label":"Inner loop"}],
             "edges":[{"from":"a","to":"ghost"}]}
            """),
            CanvasState.Empty);

        Assert.False(result.Drawn);
        Assert.Contains("ghost", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_boxes_sharing_an_id_would_make_the_arrows_ambiguous()
    {
        var result = CanvasToolParser.Parse(
            CanvasTools.ShowDiagram,
            Input("""{"nodes":[{"id":"a","label":"One"},{"id":"a","label":"Two"}],"edges":[]}"""),
            CanvasState.Empty);

        Assert.False(result.Drawn);
        Assert.Contains("ambiguous", result.Refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_well_formed_diagram_goes_up()
    {
        var result = CanvasToolParser.Parse(
            CanvasTools.ShowDiagram,
            Input("""
            {"title":"Nesting","nodes":[{"id":"o","label":"Outer"},{"id":"i","label":"Inner"}],
             "edges":[{"from":"o","to":"i","label":"runs fully each turn"}]}
            """),
            CanvasState.Empty);

        var diagram = Assert.IsType<ShowDiagram>(result.Command);
        Assert.Equal(2, diagram.Nodes.Count);
        Assert.Equal("runs fully each turn", diagram.Edges[0].Label);
    }

    [Fact]
    public void A_chart_kind_the_canvas_cannot_draw_is_refused()
    {
        var result = CanvasToolParser.Parse(
            CanvasTools.ShowChart,
            Input("""{"kind":"sankey","points":[{"label":"a","value":1},{"label":"b","value":2}]}"""),
            CanvasState.Empty);

        Assert.False(result.Drawn);
        Assert.Contains("sankey", result.Refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CanvasTools.ShowSteps, """{"items":["only one"]}""")]
    [InlineData(CanvasTools.ShowChart, """{"kind":"bar","points":[{"label":"a","value":1}]}""")]
    [InlineData(CanvasTools.ShowDiagram, """{"nodes":[{"id":"a","label":"One"}],"edges":[]}""")]
    public void One_of_something_is_not_a_list_a_chart_or_a_diagram(string tool, string json)
    {
        Assert.False(CanvasToolParser.Parse(tool, Input(json), CanvasState.Empty).Drawn);
    }

    [Fact]
    public void Too_many_points_to_follow_is_refused()
    {
        var items = string.Join(',', Enumerable.Range(1, CanvasTools.MaxSteps + 1).Select(i => $"\"point {i}\""));

        var result = CanvasToolParser.Parse(
            CanvasTools.ShowSteps, Input($$"""{"items":[{{items}}]}"""), CanvasState.Empty);

        Assert.False(result.Drawn);
    }

    [Fact]
    public void Malformed_input_refuses_rather_than_taking_the_lesson_down()
    {
        // The model produced something that does not fit the tool at all.
        var result = CanvasToolParser.Parse(
            CanvasTools.ShowChart,
            Input("""{"kind":"bar","points":[{"label":"a","value":"not a number"},{"label":"b","value":2}]}"""),
            CanvasState.Empty);

        Assert.False(result.Drawn);
        Assert.NotNull(result.Refusal);
    }

    [Fact]
    public void Clearing_empties_the_canvas()
    {
        var canvas = WithCode(ThreeLines);
        var result = CanvasToolParser.Parse(CanvasTools.ClearCanvas, Input("{}"), canvas);

        Assert.Null(canvas.Apply(result.Command!).Current);
    }

    [Fact]
    public void The_model_is_told_what_the_student_can_currently_see()
    {
        // "Look at line three" is meaningless if the tutor does not know what is on screen.
        var described = WithCode(ThreeLines, highlight: 2).Describe();

        Assert.Contains("3 lines", described, StringComparison.Ordinal);
        Assert.Contains("line 2", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_tool_the_model_is_offered_has_a_schema_and_a_description()
    {
        Assert.All(CanvasTools.All, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            var schema = JsonDocument.Parse(tool.JsonSchema).RootElement;
            Assert.Equal("object", schema.GetProperty("type").GetString());
        });
    }
}
