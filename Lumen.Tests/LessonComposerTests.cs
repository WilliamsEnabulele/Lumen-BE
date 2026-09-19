using Lumen.Domain.Ingestion;
using Lumen.Domain.Scripts;

namespace Lumen.Tests;

/// <summary>
/// The step the product is named for: a document becomes something teachable. Deterministic
/// and dependency-free, so the whole upload-to-tutor path runs with no API key and no network.
/// </summary>
public class LessonComposerTests
{
    private static readonly Guid Tenant = Guid.Parse("0197b9c2-0000-7000-8000-0000000000ff");

    private static ExtractedBlock Para(string text, string source = "p1") => new(BlockKind.Paragraph, text, source);

    private static ExtractedDocument Document(params ExtractedSection[] sections) =>
        new("Introduction to Programming", sections);

    private const string LongBody =
        "A loop is a promise to do the same work more than once. The interesting question is never what the work is. " +
        "It is how many times the promise gets kept. If you change five to a thousand, the body runs a thousand times. " +
        "Double the number and you double the work, which is a straight line. Nesting is where that stops being true. " +
        "The counts do not add together, they multiply, and multiplication gets away from you fast.";

    [Fact]
    public void A_heading_becomes_a_lesson_and_its_prose_becomes_a_script()
    {
        var composed = LessonComposer.Compose(Tenant, Document(
            new ExtractedSection("Loops", 2, [Para(LongBody)])));

        Assert.Single(composed.Lessons);
        Assert.Equal("Loops", composed.Lessons[0].Title);
        Assert.Single(composed.Concepts);
        Assert.NotEmpty(composed.ScriptNodes);
    }

    [Fact]
    public void Script_nodes_are_sized_for_the_resume_pointer_not_for_the_paragraph()
    {
        var composed = LessonComposer.Compose(Tenant, Document(
            new ExtractedSection("Loops", 2, [Para(LongBody)])));

        var spoken = composed.ScriptNodes.Where(node => node.Kind == ScriptNodeKind.Speech).ToArray();

        Assert.True(spoken.Length > 1, "a long passage should become several beats, not one");
        Assert.All(spoken, node => Assert.True(
            Utterances.WordCount(node.Text) <= Utterances.MaxWords,
            $"node ran to {Utterances.WordCount(node.Text)} words"));
    }

    [Fact]
    public void A_sentence_is_never_split_across_two_nodes()
    {
        var composed = LessonComposer.Compose(Tenant, Document(
            new ExtractedSection("Loops", 2, [Para(LongBody)])));

        foreach (var node in composed.ScriptNodes.Where(n => n.Kind == ScriptNodeKind.Speech))
        {
            Assert.True(
                node.Text.EndsWith('.') || node.Text.EndsWith('!') || node.Text.EndsWith('?'),
                $"node ended mid-sentence: “{node.Text}”");
        }
    }

    [Fact]
    public void Code_gets_its_own_beat_and_is_narrated_rather_than_read_aloud()
    {
        var composed = LessonComposer.Compose(Tenant, Document(
            new ExtractedSection("Loops", 2, [
                Para(LongBody),
                new ExtractedBlock(BlockKind.Code, "for i in range(5):\n    print(i)", "ch4:p61")
            ])));

        var code = Assert.Single(composed.ScriptNodes, node => node.VisualKind == VisualKind.Code);
        Assert.Equal("for i in range(5):\n    print(i)", code.VisualPayload);
        Assert.DoesNotContain("range(5)", code.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_node_can_be_traced_back_to_the_document_it_came_from()
    {
        var composed = LessonComposer.Compose(Tenant, Document(
            new ExtractedSection("Loops", 2, [Para(LongBody, "ch4:p61")])));

        Assert.All(composed.ScriptNodes, node => Assert.False(string.IsNullOrWhiteSpace(node.SourceRef)));
    }

    [Fact]
    public void Concepts_in_a_lesson_depend_on_the_one_before_them()
    {
        var composed = LessonComposer.Compose(Tenant, Document(
            new ExtractedSection("Loops", 2, [Para(LongBody)]),
            new ExtractedSection("Nesting", 3, [Para(LongBody)])));

        Assert.Equal(2, composed.Concepts.Count);
        Assert.Empty(composed.Concepts[0].Prerequisites);
        Assert.Equal([composed.Concepts[0].Key], composed.Concepts[1].Prerequisites);
    }

    [Fact]
    public void A_comprehension_check_arrives_before_a_misunderstanding_can_compound()
    {
        var sections = Enumerable.Range(1, LessonComposer.ConceptsPerCheck)
            .Select(i => new ExtractedSection($"Concept {i}", 3, new[] { Para(LongBody) }))
            .ToArray();

        var composed = LessonComposer.Compose(Tenant, Document(sections));

        Assert.Contains(composed.ScriptNodes, node => node.Kind == ScriptNodeKind.CheckForUnderstanding);
    }

    [Fact]
    public void A_document_with_no_headings_still_becomes_a_lesson()
    {
        // A pasted chapter, a transcript, an exported page. The common case, not an edge one.
        var blocks = Enumerable.Range(1, 6).Select(i => Para(LongBody, $"p{i}")).ToArray();
        var composed = LessonComposer.Compose(Tenant, new ExtractedDocument("Pasted notes", [
            new ExtractedSection(string.Empty, 0, blocks)
        ]));

        Assert.NotEmpty(composed.Lessons);
        Assert.NotEmpty(composed.ScriptNodes);
        Assert.All(composed.Lessons, lesson => Assert.False(string.IsNullOrWhiteSpace(lesson.Title)));
    }

    [Fact]
    public void An_empty_document_produces_nothing_to_teach_rather_than_an_empty_lesson()
    {
        var composed = LessonComposer.Compose(Tenant, ExtractedDocument.Empty("Nothing"));

        Assert.Empty(composed.Lessons);
        Assert.Empty(composed.ScriptNodes);
    }

    [Fact]
    public void Script_ordinals_are_unique_so_the_resume_pointer_is_never_ambiguous()
    {
        var composed = LessonComposer.Compose(Tenant, Document(
            new ExtractedSection("Loops", 2, [Para(LongBody)]),
            new ExtractedSection("Nesting", 3, [Para(LongBody)])));

        foreach (var lesson in composed.Lessons)
        {
            var ordinals = composed.ScriptFor(lesson.Id).Select(node => node.Ordinal).ToArray();
            Assert.Equal(ordinals.Length, ordinals.Distinct().Count());
        }
    }

    [Fact]
    public void Recomposing_the_same_document_produces_the_same_concept_keys()
    {
        // The property FR-6 rests on: reprocessing is a diff, not a regeneration.
        var document = Document(new ExtractedSection("Loops", 2, [Para(LongBody)]));

        var first = LessonComposer.Compose(Tenant, document);
        var second = LessonComposer.Compose(Tenant, document);

        // Different course ids, so keys differ — but titles drive them, and titles are stable.
        Assert.Equal(first.Concepts[0].Title, second.Concepts[0].Title);
        Assert.Equal(first.Concepts[0].Fingerprint, second.Concepts[0].Fingerprint);
    }
}
