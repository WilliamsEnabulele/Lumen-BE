using System.Text.RegularExpressions;
using Lumen.Domain.Ingestion;

namespace Lumen.Infrastructure.Extraction;

/// <summary>
/// Plain text and Markdown.
///
/// Markdown headings are real structure, so they become sections rather than being flattened
/// into prose — a document that already says where its chapters are should not have that
/// thrown away and guessed at again.
/// </summary>
public sealed partial class PlainTextExtractor : IDocumentExtractor
{
    public IReadOnlyCollection<string> Extensions { get; } = [".txt", ".md", ".markdown"];

    public ExtractedDocument Extract(Stream content, string fileName)
    {
        using var reader = new StreamReader(content);
        var text = reader.ReadToEnd();

        var sections = new List<ExtractedSection>();

        // Held separately from the sections. A document that opens with its title and goes
        // straight to the first subheading has nothing under the title, so that section is
        // never emitted — and deriving the title from the emitted sections afterwards would
        // fall back to the file name for the most ordinary document shape there is.
        var documentTitle = string.Empty;
        var heading = string.Empty;
        var level = 0;
        var blocks = new List<ExtractedBlock>();
        var paragraph = new List<string>();
        var fenced = new List<string>();
        var inFence = false;
        var lineNumber = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new ExtractedBlock(BlockKind.Paragraph, string.Join(' ', paragraph), $"line:{lineNumber}"));
            paragraph.Clear();
        }

        void FlushSection()
        {
            FlushParagraph();
            if (blocks.Count == 0) return;
            sections.Add(new ExtractedSection(heading, level, blocks.ToArray()));
            blocks.Clear();
        }

        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.TrimEnd('\r');

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inFence)
                {
                    blocks.Add(new ExtractedBlock(BlockKind.Code, string.Join('\n', fenced), $"line:{lineNumber}"));
                    fenced.Clear();
                }
                else
                {
                    FlushParagraph();
                }
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                fenced.Add(line);
                continue;
            }

            var match = HeadingPattern().Match(line);
            if (match.Success)
            {
                FlushSection();
                level = match.Groups[1].Value.Length;
                heading = match.Groups[2].Value.Trim();
                if (level == 1 && documentTitle.Length == 0) documentTitle = heading;
                continue;
            }

            if (line.Trim().Length == 0)
            {
                FlushParagraph();
                continue;
            }

            var bullet = BulletPattern().Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                blocks.Add(new ExtractedBlock(BlockKind.ListItem, bullet.Groups[1].Value.Trim(), $"line:{lineNumber}"));
                continue;
            }

            paragraph.Add(line.Trim());
        }

        if (inFence && fenced.Count > 0)
            blocks.Add(new ExtractedBlock(BlockKind.Code, string.Join('\n', fenced), $"line:{lineNumber}"));

        FlushSection();

        var title = documentTitle.Length > 0 ? documentTitle : Path.GetFileNameWithoutExtension(fileName);

        return new ExtractedDocument(title, sections);
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s*(?:[-*+]|\d+\.)\s+(.*)$")]
    private static partial Regex BulletPattern();
}
