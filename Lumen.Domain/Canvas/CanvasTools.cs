namespace Lumen.Domain.Canvas;

/// <summary>
/// The display tools the tutor can reach for while it teaches.
///
/// These descriptions are prompt surface, not documentation — they are what the model reads
/// when deciding whether this explanation needs a picture and which one. They are written to
/// discourage decoration: a canvas that changes on every sentence is as unhelpful as one that
/// never changes, and the most common failure of a model given drawing tools is using them
/// because they are there.
/// </summary>
public sealed record CanvasToolDefinition(string Name, string Description, string JsonSchema);

public static class CanvasTools
{
    public const string ShowStatement = "show_statement";
    public const string ShowSteps = "show_steps";
    public const string ShowCode = "show_code";
    public const string HighlightCode = "highlight_code";
    public const string ShowDiagram = "show_diagram";
    public const string ShowChart = "show_chart";
    public const string ShowMath = "show_math";
    public const string ClearCanvas = "clear_canvas";

    /// <summary>A statement is a line the student can hold in their head, not a paragraph.</summary>
    public const int MaxStatementLength = 160;

    public const int MaxSteps = 7;
    public const int MaxDiagramNodes = 12;
    public const int MaxChartPoints = 24;

    public static readonly IReadOnlyList<CanvasToolDefinition> All =
    [
        new(ShowStatement,
            "Put one short line on the canvas and leave it there — a definition, a rule, or the "
            + "claim you are about to defend. Use it when there is a single sentence the student "
            + "should be able to look back at. Not for a summary of what you just said.",
            """
            {"type":"object","properties":{"text":{"type":"string","description":"One line, at most 160 characters."}},"required":["text"],"additionalProperties":false}
            """),

        new(ShowSteps,
            "Put a short list on the canvas. The points appear one at a time as you speak, so say "
            + "them in this order. Use it for a sequence, a procedure, or a small set of things "
            + "being contrasted. Do not use it to restate a paragraph as bullets.",
            """
            {"type":"object","properties":{"title":{"type":"string"},"items":{"type":"array","items":{"type":"string"},"minItems":2,"maxItems":7}},"required":["items"],"additionalProperties":false}
            """),

        new(ShowCode,
            "Put a code sample on the canvas. Keep it to the smallest fragment that makes the "
            + "point. Set highlight_line to the line you are about to talk about, then move it "
            + "with highlight_code as you go rather than calling this tool again.",
            """
            {"type":"object","properties":{"language":{"type":"string"},"source":{"type":"string"},"highlight_line":{"type":"integer","description":"1-based line to light up."}},"required":["language","source"],"additionalProperties":false}
            """),

        new(HighlightCode,
            "Move the highlight to a different line of the code already on the canvas. Use this "
            + "as you walk through code so the student can see which line you mean — it is the "
            + "difference between pointing at the board and reading from notes.",
            """
            {"type":"object","properties":{"line":{"type":"integer","description":"1-based line of the code currently on the canvas."}},"required":["line"],"additionalProperties":false}
            """),

        new(ShowDiagram,
            "Draw boxes and arrows. Use it for structure and flow — how parts relate, what "
            + "depends on what, what happens in which order. This is the tool for the things "
            + "prose is worst at. Keep it under a dozen boxes; a diagram nobody can read teaches "
            + "nothing.",
            """
            {"type":"object","properties":{"title":{"type":"string"},"nodes":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"label":{"type":"string"}},"required":["id","label"],"additionalProperties":false},"minItems":2,"maxItems":12},"edges":{"type":"array","items":{"type":"object","properties":{"from":{"type":"string"},"to":{"type":"string"},"label":{"type":"string"}},"required":["from","to"],"additionalProperties":false}}},"required":["nodes","edges"],"additionalProperties":false}
            """),

        new(ShowChart,
            "Plot numbers. Use it when the shape of the data is the point — growth, comparison, "
            + "a trend. Only use real numbers from the course material or from a calculation you "
            + "have just talked through; never invent plausible-looking data to make a chart.",
            """
            {"type":"object","properties":{"kind":{"type":"string","enum":["bar","line","scatter"]},"title":{"type":"string"},"points":{"type":"array","items":{"type":"object","properties":{"label":{"type":"string"},"value":{"type":"number"}},"required":["label","value"],"additionalProperties":false},"minItems":2,"maxItems":24}},"required":["kind","points"],"additionalProperties":false}
            """),

        new(ShowMath,
            "Put an equation on the canvas, written in LaTeX. Use it when the notation itself is "
            + "what needs looking at. Say the equation aloud in words as well — a student who "
            + "cannot yet read the notation is exactly the one who needs it shown.",
            """
            {"type":"object","properties":{"latex":{"type":"string"},"caption":{"type":"string"}},"required":["latex"],"additionalProperties":false}
            """),

        new(ClearCanvas,
            "Take everything off the canvas. Use it when you have moved on and what is up there "
            + "would now be misleading. An empty canvas is better than a stale one.",
            """
            {"type":"object","properties":{},"additionalProperties":false}
            """),
    ];

    public static CanvasToolDefinition? Find(string name) =>
        All.FirstOrDefault(tool => tool.Name.Equals(name, StringComparison.Ordinal));
}
