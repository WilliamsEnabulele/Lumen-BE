using Lumen.Infrastructure.Extraction;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Lumen.Tests;

/// <summary>
/// Reading PDFs.
///
/// The fixtures are built here rather than checked in, because a binary fixture is one nobody
/// can read the diff of — a test that starts failing leaves you unable to see what changed in
/// the input. Built ones say exactly what is on the page, in the test that reads them.
/// </summary>
public class PdfExtractionTests
{
    /// <summary>A PDF whose pages are the given lines, laid out top to bottom.</summary>
    private static Stream Pdf(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 760;

            foreach (var line in lines)
            {
                if (line.Length > 0) page.AddText(line, 12, new PdfPoint(50, y), font);
                y -= 24;
            }
        }

        return new MemoryStream(builder.Build());
    }

    /// <summary>The same page with a heading put on top of it.</summary>
    private static string[] Under(string heading) => [heading, .. Body];

    private static readonly string[] Body =
    [
        "A loop is a promise that a block of code will run more than once,",
        "and a promise about when it will stop. Everything hard about loops",
        "is the second half of that sentence. The counted loop runs a fixed",
        "number of times, which is the limit minus the start.",
    ];

    [Fact]
    public void The_words_on_the_page_come_back()
    {
        var extracted = new PdfExtractor().Extract(Pdf(Body), "loops.pdf");

        var text = string.Join(' ', extracted.Sections.SelectMany(s => s.Blocks).Select(b => b.Text));

        Assert.Contains("a promise", text, StringComparison.Ordinal);
        Assert.Contains("the limit minus the start", text, StringComparison.Ordinal);
        Assert.True(extracted.WordCount > 30, $"only {extracted.WordCount} words came back");
    }

    [Fact]
    public void A_shouted_line_is_a_heading_and_names_the_document()
    {
        var extracted = new PdfExtractor().Extract(Pdf(Under("COUNTED LOOPS")), "chapter-4.pdf");

        Assert.Equal("COUNTED LOOPS", extracted.Title);
        Assert.Contains(extracted.Sections, section => section.Heading == "COUNTED LOOPS");
    }

    [Fact]
    public void A_numbered_heading_is_one_too()
    {
        var extracted = new PdfExtractor().Extract(Pdf(Under("4.2 The conditional loop")), "chapter-4.pdf");

        Assert.Contains(extracted.Sections, section => section.Heading == "4.2 The conditional loop");
    }

    [Fact]
    public void Every_block_says_which_page_it_came_from()
    {
        var extracted = new PdfExtractor().Extract(Pdf(Body, Body), "two-pages.pdf");

        var refs = extracted.Sections.SelectMany(s => s.Blocks).Select(b => b.SourceRef).Distinct().ToArray();

        Assert.Contains("page:1", refs);
        Assert.Contains("page:2", refs);
    }

    [Fact]
    public void A_document_with_no_headings_falls_back_to_its_pages()
    {
        // Running prose has no structure but its pages, and inventing more would be inventing.
        var extracted = new PdfExtractor().Extract(Pdf(Body, Body), "report.pdf");

        Assert.Equal(2, extracted.Sections.Count);
        Assert.Equal("Page 1", extracted.Sections[0].Heading);
        Assert.Equal("report", extracted.Title);
    }

    [Fact]
    public void A_scan_is_refused_by_name_rather_than_taught_from()
    {
        // The failure that matters most here. A photographed textbook has no text layer, and
        // accepting one builds a course out of nothing at all.
        var blank = new PdfDocumentBuilder();
        blank.AddPage(PageSize.A4);
        blank.AddPage(PageSize.A4);

        var failure = Assert.Throws<InvalidDataException>(
            () => new PdfExtractor().Extract(new MemoryStream(blank.Build()), "scan.pdf"));

        Assert.Contains("OCR", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_too_thin_to_teach_from_says_so_instead()
    {
        var failure = Assert.Throws<InvalidDataException>(() => new PdfExtractor().Extract(
            Pdf(new[] { "Loops are useful and worth learning about in some detail one day soon." }),
            "note.pdf"));

        Assert.DoesNotContain("OCR", failure.Message, StringComparison.Ordinal);
        Assert.Contains("not enough", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdfs_are_offered_as_a_supported_format()
    {
        var extractors = new DocumentExtractors([new PlainTextExtractor(), new PdfExtractor()]);

        Assert.Contains(".pdf", extractors.SupportedExtensions);
        Assert.IsType<PdfExtractor>(extractors.For("chapter.pdf"));
        Assert.IsType<PdfExtractor>(extractors.For("CHAPTER.PDF"));
    }
}

/// <summary>
/// Heading detection on its own, where the interesting cases live. Asymmetric on purpose: a
/// missed heading costs a little structure, an invented one puts a sentence fragment where a
/// concept title should be, and that fragment is what the lesson plan names.
/// </summary>
public class PdfHeadingTests
{
    [Theory]
    [InlineData("COUNTED LOOPS")]
    [InlineData("4.2 The conditional loop")]
    [InlineData("4 Loops")]
    [InlineData("Chapter 3 Breaking early")]
    [InlineData("OFF-BY-ONE ERRORS")]
    public void Shouted_or_numbered_lines_are_headings(string line)
    {
        Assert.True(PdfExtractor.LooksLikeHeading(line), $"'{line}' should have been a heading");
    }

    [Theory]
    [InlineData("The loop runs five times.")]
    [InlineData("A loop is a promise that a block of code will run more than once, and a promise about when it stops")]
    [InlineData("This is Title Case And Still A Sentence")]
    [InlineData("the counted loop")]
    [InlineData("For example:")]
    [InlineData("")]
    public void Sentences_are_not(string line)
    {
        Assert.False(PdfExtractor.LooksLikeHeading(line), $"'{line}' should not have been a heading");
    }

    [Fact]
    public void A_shout_that_runs_on_is_a_sentence_in_capitals()
    {
        Assert.False(PdfExtractor.LooksLikeHeading(
            "A LOOP IS A PROMISE THAT A BLOCK OF CODE WILL RUN MORE THAN ONCE AND STOP"));
    }
}
