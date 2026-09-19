using System.Text.Json.Serialization;

namespace Lumen.Domain.Canvas;

/// <summary>
/// Something the tutor has put on the canvas.
///
/// These are the result of the model calling a display tool mid-explanation — it decides what
/// is worth drawing and when, the same way a lecturer decides to turn to the board. They are
/// not precomputed from the document, because what needs illustrating depends on how the
/// explanation is actually going and on what the student just asked.
/// </summary>
/// <remarks>
/// The polymorphic contract is here rather than in a serializer because the tool name is
/// already the discriminator everywhere else — it is what the model calls, what the parser
/// dispatches on, and what the client switches on to decide which shape to draw. Inventing a
/// second one for storage would mean a canvas that round-trips through disk is not obviously
/// the same canvas that went in.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tool")]
[JsonDerivedType(typeof(ShowStatement), CanvasTools.ShowStatement)]
[JsonDerivedType(typeof(ShowSteps), CanvasTools.ShowSteps)]
[JsonDerivedType(typeof(ShowCode), CanvasTools.ShowCode)]
[JsonDerivedType(typeof(HighlightCode), CanvasTools.HighlightCode)]
[JsonDerivedType(typeof(ShowDiagram), CanvasTools.ShowDiagram)]
[JsonDerivedType(typeof(ShowChart), CanvasTools.ShowChart)]
[JsonDerivedType(typeof(ShowMath), CanvasTools.ShowMath)]
[JsonDerivedType(typeof(ClearCanvas), CanvasTools.ClearCanvas)]
public abstract record CanvasCommand
{
    /// <summary>
    /// Ignored when serialising because the discriminator above already writes it. Two "tool"
    /// properties on the same object is not a style question — it is an exception at runtime.
    ///
    /// The attribute has to be repeated on every override below. It does not carry from an
    /// abstract member to the concrete ones, so the base being marked here proves nothing
    /// about the types that are actually serialised.
    /// </summary>
    [JsonIgnore]
    public abstract string Tool { get; }
}

/// <summary>A line worth leaving up: a definition, a rule, the thing being claimed.</summary>
public sealed record ShowStatement(string Text) : CanvasCommand
{
    [JsonIgnore]
    public override string Tool => CanvasTools.ShowStatement;
}

/// <summary>Points that arrive one at a time, in step with being said.</summary>
public sealed record ShowSteps(string? Title, IReadOnlyList<string> Items) : CanvasCommand
{
    [JsonIgnore]
    public override string Tool => CanvasTools.ShowSteps;
}

public sealed record ShowCode(string Language, string Source, int? HighlightLine) : CanvasCommand
{
    [JsonIgnore]
    public override string Tool => CanvasTools.ShowCode;

    public int LineCount => Source.Split('\n').Length;
}

/// <summary>Moves the lit line as the explanation walks through code already on the canvas.</summary>
public sealed record HighlightCode(int Line) : CanvasCommand
{
    [JsonIgnore]
    public override string Tool => CanvasTools.HighlightCode;
}

public sealed record DiagramNode(string Id, string Label);

public sealed record DiagramEdge(string From, string To, string? Label);

/// <summary>Boxes and arrows — how parts of a thing relate, which prose is bad at.</summary>
public sealed record ShowDiagram(string? Title, IReadOnlyList<DiagramNode> Nodes, IReadOnlyList<DiagramEdge> Edges)
    : CanvasCommand
{
    [JsonIgnore]
    public override string Tool => CanvasTools.ShowDiagram;
}

public sealed record ChartPoint(string Label, double Value);

public sealed record ShowChart(string Kind, string? Title, IReadOnlyList<ChartPoint> Points) : CanvasCommand
{
    [JsonIgnore]
    public override string Tool => CanvasTools.ShowChart;

    public static readonly IReadOnlyList<string> Kinds = ["bar", "line", "scatter"];
}

public sealed record ShowMath(string Latex, string? Caption) : CanvasCommand
{
    [JsonIgnore]
    public override string Tool => CanvasTools.ShowMath;
}

public sealed record ClearCanvas : CanvasCommand
{
    [JsonIgnore]
    public override string Tool => CanvasTools.ClearCanvas;
}

/// <summary>
/// What is on the canvas right now.
///
/// Held server-side as well as drawn client-side, because the tutor has to be able to reason
/// about it: "look at line three" is meaningless if the model does not know what is on screen,
/// and a highlight aimed at code that was cleared two turns ago is the kind of error that
/// makes a tutor look like it is not paying attention.
/// </summary>
public sealed record CanvasState(CanvasCommand? Current)
{
    public static readonly CanvasState Empty = new((CanvasCommand?)null);

    public ShowCode? CodeOnScreen => Current as ShowCode;

    public CanvasState Apply(CanvasCommand command) => command switch
    {
        ClearCanvas => Empty,
        // A highlight moves the line on the code already up rather than replacing it.
        HighlightCode highlight when Current is ShowCode code =>
            new CanvasState(code with { HighlightLine = highlight.Line }),
        _ => new CanvasState(command),
    };

    /// <summary>A short description for the model, so it knows what the student can see.</summary>
    public string Describe() => Current switch
    {
        null => "The canvas is empty.",
        ShowStatement statement => $"A statement is on the canvas: \"{statement.Text}\"",
        ShowSteps steps => $"A list of {steps.Items.Count} points is on the canvas.",
        ShowCode code => code.HighlightLine is int line
            ? $"{code.LineCount} lines of {code.Language} are on the canvas, with line {line} highlighted."
            : $"{code.LineCount} lines of {code.Language} are on the canvas, nothing highlighted.",
        ShowDiagram diagram => $"A diagram of {diagram.Nodes.Count} nodes is on the canvas.",
        ShowChart chart => $"A {chart.Kind} chart of {chart.Points.Count} points is on the canvas.",
        ShowMath math => $"An equation is on the canvas: {math.Latex}",
        _ => "Something is on the canvas."
    };
}
