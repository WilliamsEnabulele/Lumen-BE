using System.Text.Json;

namespace Lumen.Domain.Canvas;

/// <summary>
/// Turns a model's tool call into something safe to draw, or a refusal saying why not.
///
/// This exists because a schema is not enough. A schema can say `line` is an integer; it
/// cannot say the code on the canvas has four lines, so line nine is meaningless. The failures
/// worth catching here are all of that shape — coherent JSON describing an incoherent picture.
///
/// Refusals are written back to the model as tool errors rather than swallowed, because a
/// tutor that quietly fails to draw what it just said it would draw is worse than one that is
/// told it got the line number wrong and corrects itself out loud.
/// </summary>
public static class CanvasToolParser
{
    public static CanvasToolResult Parse(string toolName, JsonElement input, CanvasState canvas)
    {
        if (CanvasTools.Find(toolName) is null)
            return CanvasToolResult.Refused($"There is no display tool called '{toolName}'.");

        try
        {
            return toolName switch
            {
                CanvasTools.ShowStatement => Statement(input),
                CanvasTools.ShowSteps => Steps(input),
                CanvasTools.ShowCode => Code(input),
                CanvasTools.HighlightCode => Highlight(input, canvas),
                CanvasTools.ShowDiagram => Diagram(input),
                CanvasTools.ShowChart => Chart(input),
                CanvasTools.ShowMath => Math(input),
                CanvasTools.ClearCanvas => CanvasToolResult.Drew(new ClearCanvas()),
                _ => CanvasToolResult.Refused($"There is no display tool called '{toolName}'.")
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            // The model produced JSON that does not fit the tool. Say so rather than crashing
            // the lesson — it can try again on the next turn.
            return CanvasToolResult.Refused($"That did not match what {toolName} expects: {exception.Message}");
        }
    }

    private static CanvasToolResult Statement(JsonElement input)
    {
        var text = Text(input, "text");
        if (string.IsNullOrWhiteSpace(text))
            return CanvasToolResult.Refused("A statement needs some text.");

        if (text.Length > CanvasTools.MaxStatementLength)
            return CanvasToolResult.Refused(
                $"That is {text.Length} characters. A statement is one line the student can hold in "
                + $"their head — keep it under {CanvasTools.MaxStatementLength}, or use show_steps.");

        return CanvasToolResult.Drew(new ShowStatement(text.Trim()));
    }

    private static CanvasToolResult Steps(JsonElement input)
    {
        var items = Strings(input, "items");
        if (items.Count < 2)
            return CanvasToolResult.Refused("A list of one point is a statement — use show_statement.");

        if (items.Count > CanvasTools.MaxSteps)
            return CanvasToolResult.Refused(
                $"{items.Count} points is more than a student can follow at once; keep it to {CanvasTools.MaxSteps}.");

        if (items.Any(string.IsNullOrWhiteSpace))
            return CanvasToolResult.Refused("One of those points is empty.");

        return CanvasToolResult.Drew(new ShowSteps(Optional(input, "title"), items));
    }

    private static CanvasToolResult Code(JsonElement input)
    {
        var source = Text(input, "source");
        if (string.IsNullOrWhiteSpace(source))
            return CanvasToolResult.Refused("There is no code to show.");

        var language = Optional(input, "language") ?? "text";
        var lines = source.Split('\n').Length;

        int? highlight = null;
        if (input.TryGetProperty("highlight_line", out var raw) && raw.ValueKind == JsonValueKind.Number)
        {
            var line = raw.GetInt32();
            if (line < 1 || line > lines)
                return CanvasToolResult.Refused(
                    $"Line {line} is outside this snippet, which has {lines} lines.");
            highlight = line;
        }

        return CanvasToolResult.Drew(new ShowCode(language, source, highlight));
    }

    private static CanvasToolResult Highlight(JsonElement input, CanvasState canvas)
    {
        if (canvas.CodeOnScreen is not { } code)
            return CanvasToolResult.Refused(
                "There is no code on the canvas to highlight. Call show_code first.");

        if (!input.TryGetProperty("line", out var raw) || raw.ValueKind != JsonValueKind.Number)
            return CanvasToolResult.Refused("Which line?");

        var line = raw.GetInt32();
        if (line < 1 || line > code.LineCount)
            return CanvasToolResult.Refused(
                $"Line {line} is outside the code on the canvas, which has {code.LineCount} lines.");

        return CanvasToolResult.Drew(new HighlightCode(line));
    }

    private static CanvasToolResult Diagram(JsonElement input)
    {
        var nodes = new List<DiagramNode>();
        if (input.TryGetProperty("nodes", out var rawNodes) && rawNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in rawNodes.EnumerateArray())
            {
                var id = Text(node, "id");
                var label = Text(node, "label");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label))
                    return CanvasToolResult.Refused("Every box needs an id and a label.");
                nodes.Add(new DiagramNode(id, label));
            }
        }

        if (nodes.Count < 2)
            return CanvasToolResult.Refused("A diagram of one box is not a diagram.");

        if (nodes.Count > CanvasTools.MaxDiagramNodes)
            return CanvasToolResult.Refused(
                $"{nodes.Count} boxes is more than anyone can read; keep it to {CanvasTools.MaxDiagramNodes}.");

        if (nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != nodes.Count)
            return CanvasToolResult.Refused("Two boxes share an id, so the arrows would be ambiguous.");

        var ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = new List<DiagramEdge>();

        if (input.TryGetProperty("edges", out var rawEdges) && rawEdges.ValueKind == JsonValueKind.Array)
        {
            foreach (var edge in rawEdges.EnumerateArray())
            {
                var from = Text(edge, "from");
                var to = Text(edge, "to");

                // An arrow to a box that does not exist draws nothing and reads as a bug.
                if (!ids.Contains(from)) return CanvasToolResult.Refused($"There is no box called '{from}'.");
                if (!ids.Contains(to)) return CanvasToolResult.Refused($"There is no box called '{to}'.");

                edges.Add(new DiagramEdge(from, to, Optional(edge, "label")));
            }
        }

        return CanvasToolResult.Drew(new ShowDiagram(Optional(input, "title"), nodes, edges));
    }

    private static CanvasToolResult Chart(JsonElement input)
    {
        var kind = (Optional(input, "kind") ?? "bar").ToLowerInvariant();
        if (!ShowChart.Kinds.Contains(kind))
            return CanvasToolResult.Refused(
                $"'{kind}' is not a chart this canvas draws. Use {string.Join(", ", ShowChart.Kinds)}.");

        var points = new List<ChartPoint>();
        if (input.TryGetProperty("points", out var raw) && raw.ValueKind == JsonValueKind.Array)
        {
            foreach (var point in raw.EnumerateArray())
            {
                var label = Text(point, "label");
                if (!point.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number)
                    return CanvasToolResult.Refused($"The point '{label}' has no number.");
                points.Add(new ChartPoint(label, value.GetDouble()));
            }
        }

        if (points.Count < 2)
            return CanvasToolResult.Refused("A chart of one point is not a chart.");

        if (points.Count > CanvasTools.MaxChartPoints)
            return CanvasToolResult.Refused($"Keep a chart to {CanvasTools.MaxChartPoints} points.");

        return CanvasToolResult.Drew(new ShowChart(kind, Optional(input, "title"), points));
    }

    private static CanvasToolResult Math(JsonElement input)
    {
        var latex = Text(input, "latex");
        return string.IsNullOrWhiteSpace(latex)
            ? CanvasToolResult.Refused("There is no equation to show.")
            : CanvasToolResult.Drew(new ShowMath(latex.Trim(), Optional(input, "caption")));
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? Optional(JsonElement element, string property)
    {
        var text = Text(element, property);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static IReadOnlyList<string> Strings(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
    }
}

/// <param name="Refusal">
/// Written for the model, not the student. It goes back as a tool error so the next turn can
/// correct itself — which is why each one says what was wrong rather than just "invalid".
/// </param>
public sealed record CanvasToolResult(CanvasCommand? Command, string? Refusal)
{
    public bool Drawn => Command is not null;

    public static CanvasToolResult Drew(CanvasCommand command) => new(command, null);

    public static CanvasToolResult Refused(string reason) => new(null, reason);
}
