using System.Text;
using Lumen.Domain.Ingestion;
using Lumen.Domain.Scripts;
using Lumen.Infrastructure.Extraction;

namespace Lumen.Tests;

/// <summary>
/// The whole upload path, end to end, with no network and no API key: bytes in, a teachable
/// lesson out. This is the test that says the product's central claim actually happens.
/// </summary>
public class ExtractionTests
{
    private static readonly Guid Tenant = Guid.Parse("0197b9c2-0000-7000-8000-0000000000ff");

    private const string Markdown = """
# Introduction to Programming

## Loops

A loop is a promise to do the same work more than once. The interesting question is never
what the work is. It is how many times the promise gets kept. If you change five to a
thousand, the body runs a thousand times, so double the number and you double the work.

```python
for i in range(5):
    print(i)
```

## Nesting

Nesting puts one loop inside another. The outer loop takes its turns, and every one of those
turns runs the entire inner loop from the start. The counts do not add together, they
multiply, and multiplication gets away from you faster than people expect it to.

- The outer loop sets how many times
- The inner loop runs fully on each one
- Multiply them to get the real cost
""";

    private static Stream Bytes(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Markdown_headings_become_the_structure_rather_than_being_flattened()
    {
        var extracted = new PlainTextExtractor().Extract(Bytes(Markdown), "course.md");

        Assert.Equal("Introduction to Programming", extracted.Title);
        Assert.Contains(extracted.Sections, section => section.Heading == "Loops");
        Assert.Contains(extracted.Sections, section => section.Heading == "Nesting");
    }

    [Fact]
    public void A_fenced_block_is_code_not_prose()
    {
        var extracted = new PlainTextExtractor().Extract(Bytes(Markdown), "course.md");

        var code = extracted.Sections.SelectMany(section => section.Blocks)
            .Single(block => block.Kind == BlockKind.Code);

        Assert.Contains("for i in range(5):", code.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Bullets_are_list_items_so_the_canvas_can_reveal_them_one_at_a_time()
    {
        var extracted = new PlainTextExtractor().Extract(Bytes(Markdown), "course.md");

        Assert.Equal(3, extracted.Sections.SelectMany(s => s.Blocks).Count(b => b.Kind == BlockKind.ListItem));
    }

    [Fact]
    public void A_document_becomes_a_lesson_a_student_could_actually_sit_through()
    {
        var extracted = new PlainTextExtractor().Extract(Bytes(Markdown), "course.md");
        var composed = LessonComposer.Compose(Tenant, extracted);

        Assert.Equal("Introduction to Programming", composed.Course.Title);
        Assert.True(composed.Lessons.Count >= 2);
        Assert.True(composed.ScriptNodes.Count >= 4);

        // Something to look at, and something to say about it.
        Assert.Contains(composed.ScriptNodes, node => node.VisualKind == VisualKind.Code);
        Assert.Contains(composed.ScriptNodes, node => node.VisualKind == VisualKind.Steps);
        Assert.All(composed.ScriptNodes, node => Assert.False(string.IsNullOrWhiteSpace(node.Text)));
    }

    [Fact]
    public void An_unsupported_format_is_named_rather_than_silently_producing_nothing()
    {
        var extractors = new DocumentExtractors([new PlainTextExtractor(), new WordExtractor(), new SlidesExtractor()]);

        Assert.Null(extractors.For("scanned-chapter.pdf"));
        Assert.NotNull(extractors.For("chapter.docx"));
        Assert.NotNull(extractors.For("deck.pptx"));
        Assert.NotNull(extractors.For("notes.md"));
    }

    [Fact]
    public void A_file_that_is_not_the_format_its_name_claims_fails_loudly()
    {
        Assert.ThrowsAny<Exception>(() => new WordExtractor().Extract(Bytes("not a zip"), "pretend.docx"));
    }
}
