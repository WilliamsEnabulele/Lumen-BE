using System.IO.Compression;
using System.Xml.Linq;
using Lumen.Domain.Ingestion;

namespace Lumen.Infrastructure.Extraction;

/// <summary>
/// Word documents.
///
/// A .docx is a zip of XML, and both are in the base class library, so this needs no package.
/// That is worth the slightly longer code: an extractor with no dependency cannot fail to
/// restore, and the upload path stays runnable by anyone who clones the repository.
/// </summary>
public sealed class WordExtractor : IDocumentExtractor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public IReadOnlyCollection<string> Extensions { get; } = [".docx"];

    public ExtractedDocument Extract(Stream content, string fileName)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("This does not look like a Word document: no word/document.xml.");

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        var sections = new List<ExtractedSection>();

        // Held separately for the same reason as in PlainTextExtractor: a Title or Heading 1
        // followed straight by Heading 2 emits no section of its own.
        var documentTitle = string.Empty;
        var heading = string.Empty;
        var level = 0;
        var blocks = new List<ExtractedBlock>();
        var index = 0;

        void Flush()
        {
            if (blocks.Count == 0) return;
            sections.Add(new ExtractedSection(heading, level, blocks.ToArray()));
            blocks.Clear();
        }

        foreach (var paragraph in document.Descendants(W + "p"))
        {
            index++;
            var text = string.Concat(paragraph.Descendants(W + "t").Select(run => run.Value)).Trim();
            if (text.Length == 0) continue;

            var style = paragraph.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value ?? string.Empty;

            if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                heading = text;
                level = int.TryParse(style[7..], out var parsed) ? parsed : 1;
                if (level == 1 && documentTitle.Length == 0) documentTitle = heading;
                continue;
            }

            if (style.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                heading = text;
                level = 1;
                if (documentTitle.Length == 0) documentTitle = heading;
                continue;
            }

            var kind = style.Contains("ListParagraph", StringComparison.OrdinalIgnoreCase)
                ? BlockKind.ListItem
                : BlockKind.Paragraph;

            blocks.Add(new ExtractedBlock(kind, text, $"para:{index}"));
        }

        Flush();

        var title = documentTitle.Length > 0 ? documentTitle : Path.GetFileNameWithoutExtension(fileName);

        return new ExtractedDocument(title, sections);
    }
}

/// <summary>
/// PowerPoint decks.
///
/// A slide is already a section — its title is a heading and its bullets are the points — so a
/// deck needs less guessing than prose does. Slides are read in file order, which is the order
/// they are presented in.
/// </summary>
public sealed class SlidesExtractor : IDocumentExtractor
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public IReadOnlyCollection<string> Extensions { get; } = [".pptx"];

    public ExtractedDocument Extract(Stream content, string fileName)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);

        var slides = archive.Entries
            .Where(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal)
                            && entry.FullName.EndsWith(".xml", StringComparison.Ordinal))
            .OrderBy(entry => SlideNumber(entry.FullName))
            .ToArray();

        if (slides.Length == 0)
            throw new InvalidDataException("This does not look like a PowerPoint file: no slides found.");

        var sections = new List<ExtractedSection>();

        foreach (var slide in slides)
        {
            var number = SlideNumber(slide.FullName);
            using var stream = slide.Open();
            var document = XDocument.Load(stream);

            // Each shape on the slide is a text frame; the first is conventionally the title.
            var paragraphs = document.Descendants(A + "p")
                .Select(paragraph => string.Concat(paragraph.Descendants(A + "t").Select(run => run.Value)).Trim())
                .Where(text => text.Length > 0)
                .ToArray();

            if (paragraphs.Length == 0) continue;

            var heading = paragraphs[0];
            var blocks = paragraphs.Skip(1)
                .Select(text => new ExtractedBlock(BlockKind.ListItem, text, $"slide:{number}"))
                .ToArray();

            // A slide with a title and nothing else is a section break, not a lesson.
            if (blocks.Length == 0) continue;

            sections.Add(new ExtractedSection(heading, 2, blocks));
        }

        var title = sections.Count > 0 ? sections[0].Heading : Path.GetFileNameWithoutExtension(fileName);
        return new ExtractedDocument(title, sections);
    }

    private static int SlideNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }
}
