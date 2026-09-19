using System.Text.RegularExpressions;
using Lumen.Domain.Ingestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Lumen.Infrastructure.Extraction;

/// <summary>
/// PDFs — the format people actually have.
///
/// This is the one extractor with a dependency, and the reason is worth stating because the
/// others make a point of not having one. A .docx is a zip of XML and a .pptx is the same, so
/// reading them needs only the base class library. A PDF is a graphics format that happens to
/// contain glyphs: text is positioned, not flowed, encodings are per-font, and the reading
/// order in the file is frequently not the reading order on the page. Half a PDF parser does
/// not fail loudly — it produces plausible, scrambled text, which becomes a plausible,
/// scrambled lesson. Borrowing a real one is the honest choice.
///
/// What is done here rather than borrowed is the part that is about teaching: deciding what
/// counts as a heading, and refusing a document there is nothing to teach from.
/// </summary>
public sealed partial class PdfExtractor : IDocumentExtractor
{
    /// <summary>Below this there is no lesson in the file, whatever the reason.</summary>
    public const int MinimumWords = 30;

    /// <summary>
    /// Below this per page, the text layer is missing rather than thin — which is what a scan
    /// looks like. Photocopied and photographed textbooks are extremely common, and the worst
    /// possible outcome is accepting one and building a course out of nothing.
    /// </summary>
    public const int MinimumWordsPerPage = 5;

    public IReadOnlyCollection<string> Extensions { get; } = [".pdf"];

    public ExtractedDocument Extract(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);

        using var document = Open(buffer.ToArray());

        var lines = new List<PdfLine>();

        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page);
            if (string.IsNullOrWhiteSpace(text)) continue;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                lines.Add(line.Length == 0
                    ? new PdfLine(page.Number, string.Empty, false)
                    : new PdfLine(page.Number, line, LooksLikeHeading(line)));
            }
        }

        var words = lines.Sum(line => line.WordCount);
        Refuse(words, document.NumberOfPages);

        var sections = lines.Any(line => line.IsHeading) ? ByHeading(lines) : ByPage(lines);

        return new ExtractedDocument(Title(document, sections, fileName), sections);
    }

    private static PdfDocument Open(byte[] bytes)
    {
        try
        {
            // Lenient, and skipping text whose font is missing rather than throwing: a real
            // upload is frequently a slightly broken PDF, and losing a few glyphs beats
            // refusing a whole chapter over one corrupt font.
            return PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new InvalidDataException(
                "This PDF is password-protected, so it cannot be read. Save an unprotected copy and upload that.");
        }
    }

    /// <summary>
    /// Says no, with the reason, rather than producing an empty course.
    ///
    /// The distinction between the two messages is the whole value of this check: a scan needs
    /// OCR and a thin document needs more material, and telling someone the wrong one sends
    /// them off to fix something that was never the problem.
    /// </summary>
    private static void Refuse(int words, int pages)
    {
        if (pages > 0 && words < pages * MinimumWordsPerPage)
            throw new InvalidDataException(
                "There is almost no text in this PDF — it looks like a scan or a photograph of the pages "
                + "rather than a document. Run it through OCR first, or upload the original file.");

        if (words < MinimumWords)
            throw new InvalidDataException(
                $"This PDF holds only {words} words, which is not enough to build a lesson from.");
    }

    /// <summary>
    /// Sections split at headings, each block still carrying the page it came from.
    /// </summary>
    private static IReadOnlyList<ExtractedSection> ByHeading(IReadOnlyList<PdfLine> lines)
    {
        var sections = new List<ExtractedSection>();
        var blocks = new List<ExtractedBlock>();
        var paragraph = new List<PdfLine>();
        var heading = string.Empty;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(Paragraph(paragraph));
            paragraph.Clear();
        }

        void FlushSection()
        {
            FlushParagraph();
            if (blocks.Count == 0) return;
            sections.Add(new ExtractedSection(heading, 1, blocks.ToArray()));
            blocks.Clear();
        }

        foreach (var line in lines)
        {
            if (line.IsHeading)
            {
                FlushSection();
                heading = line.Text;
                continue;
            }

            if (line.Text.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            paragraph.Add(line);
        }

        FlushSection();
        return sections;
    }

    /// <summary>
    /// The fallback for a document with no headings anywhere — a report, a paper, a scan of
    /// running prose. A page is the only structure such a file actually has, so pretending to
    /// find more would be inventing it.
    /// </summary>
    private static IReadOnlyList<ExtractedSection> ByPage(IReadOnlyList<PdfLine> lines)
    {
        var sections = new List<ExtractedSection>();

        foreach (var page in lines.GroupBy(line => line.Page).OrderBy(group => group.Key))
        {
            var blocks = new List<ExtractedBlock>();
            var paragraph = new List<PdfLine>();

            foreach (var line in page)
            {
                if (line.Text.Length == 0)
                {
                    if (paragraph.Count > 0) blocks.Add(Paragraph(paragraph));
                    paragraph.Clear();
                    continue;
                }

                paragraph.Add(line);
            }

            if (paragraph.Count > 0) blocks.Add(Paragraph(paragraph));
            if (blocks.Count > 0) sections.Add(new ExtractedSection($"Page {page.Key}", 1, blocks.ToArray()));
        }

        return sections;
    }

    private static ExtractedBlock Paragraph(IReadOnlyList<PdfLine> lines) =>
        new(BlockKind.Paragraph,
            string.Join(' ', lines.Select(line => line.Text)),
            $"page:{lines[0].Page}");

    /// <summary>
    /// Deliberately conservative, because the two failures are not symmetrical. A missed
    /// heading merges two sections, which costs a little structure. An invented one splits a
    /// paragraph in half and puts a sentence fragment where a title should be, and that
    /// fragment ends up named as a concept in the lesson plan.
    ///
    /// So: short, not punctuated like a sentence, and either shouted or numbered. Title Case
    /// is not enough — "The loop runs five times" is a sentence, and most false positives look
    /// exactly like that.
    /// </summary>
    public static bool LooksLikeHeading(string line)
    {
        var text = line.Trim();
        if (text.Length is < 2 or > 90) return false;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 12) return false;

        if (".,;:".Contains(text[^1])) return false;

        if (NumberedPattern().IsMatch(text)) return true;

        var letters = text.Where(char.IsLetter).ToArray();
        return letters.Length > 0 && letters.All(char.IsUpper);
    }

    [GeneratedRegex(@"^(?:\d+(?:\.\d+)*[.)]?|(?:Chapter|Section|Part|Unit|Lesson|Module)\s+\w+[.:)]?)\s+\S")]
    private static partial Regex NumberedPattern();

    /// <summary>
    /// The document's own title if it has a usable one, then the first heading, then the file
    /// name. PDF metadata titles are frequently the source file path or the name of whatever
    /// produced the file, so a title that looks like a file name is treated as absent.
    /// </summary>
    private static string Title(PdfDocument document, IReadOnlyList<ExtractedSection> sections, string fileName)
    {
        var declared = document.Information.Title?.Trim();

        if (!string.IsNullOrEmpty(declared)
            && !declared.Contains(".pdf", StringComparison.OrdinalIgnoreCase)
            && !declared.Contains(".doc", StringComparison.OrdinalIgnoreCase)
            && !declared.Contains('\\')
            && !declared.Contains('/'))
        {
            return declared;
        }

        var heading = sections.FirstOrDefault(section => section.Heading.Length > 0)?.Heading;

        return !string.IsNullOrEmpty(heading) && !heading.StartsWith("Page ", StringComparison.Ordinal)
            ? heading
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private sealed record PdfLine(int Page, string Text, bool IsHeading)
    {
        public int WordCount => Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
