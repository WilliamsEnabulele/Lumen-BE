using Lumen.Domain.Courses;
using Lumen.Domain.Scripts;

namespace Lumen.Domain.Ingestion;

/// <summary>
/// Turns an extracted document into something teachable: modules, lessons, concepts with
/// prerequisite edges, and the script the tutor speaks.
///
/// This is the step the product is named for. It is deterministic and has no external
/// dependency on purpose — a document becomes a lesson with no API key, no network and no
/// model, which means the whole upload-to-tutor path is runnable by anyone who clones the
/// repository. A flow nobody can run is a flow nobody checks.
///
/// A model improves the *phrasing* later; it does not get to decide the structure, because
/// structure is what an instructor reviews and what a wrong answer damages permanently.
/// </summary>
public static class LessonComposer
{
    /// <summary>A comprehension check after this many concepts. Often enough to catch a
    /// misunderstanding before it compounds, rare enough not to feel like an exam.</summary>
    public const int ConceptsPerCheck = 3;

    public static ComposedCourse Compose(Guid tenantId, ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var course = new Course
        {
            TenantId = tenantId,
            Title = string.IsNullOrWhiteSpace(document.Title) ? "Untitled course" : document.Title.Trim(),
            State = CoursePublicationState.AwaitingReview,
        };

        var modules = new List<Module>();
        var lessons = new List<Lesson>();
        var concepts = new List<Concept>();
        var nodes = new List<ScriptNode>();

        Module? module = null;
        Lesson? lesson = null;
        var conceptsInLesson = 0;
        var previousConceptKey = (string?)null;

        foreach (var section in Sectioned(document))
        {
            if (section.Level <= 1 || module is null)
            {
                module = new Module
                {
                    TenantId = tenantId,
                    CourseId = course.Id,
                    Ordinal = modules.Count + 1,
                    Title = Titled(section.Heading, $"Part {modules.Count + 1}"),
                };
                modules.Add(module);
                lesson = null;
            }

            if (lesson is null || section.Level <= 2)
            {
                lesson = new Lesson
                {
                    TenantId = tenantId,
                    CourseId = course.Id,
                    ModuleId = module.Id,
                    Ordinal = lessons.Count + 1,
                    Title = Titled(section.Heading, module.Title),
                };
                lessons.Add(lesson);
                conceptsInLesson = 0;
            }

            var body = string.Join(
                ' ',
                section.Blocks.Where(block => block.Kind is BlockKind.Paragraph or BlockKind.ListItem)
                    .Select(block => block.Text));

            var concept = new Concept
            {
                TenantId = tenantId,
                CourseId = course.Id,
                LessonId = lesson.Id,
                Title = Titled(section.Heading, FirstWordsOf(body)),
                Body = body,
                SourceRef = section.Blocks.FirstOrDefault()?.SourceRef,
            };
            concept.Key = ConceptKey.From(course.Id, concept.Title);
            concept.Fingerprint = ContentFingerprint.Of(body);

            // A lesson teaches its concepts in order, so each depends on the one before it.
            // Edges across lessons are a review decision, not something to infer from layout.
            if (previousConceptKey is not null && conceptsInLesson > 0)
                concept.Prerequisites.Add(previousConceptKey);

            concepts.Add(concept);
            conceptsInLesson++;
            previousConceptKey = concept.Key;

            nodes.AddRange(ScriptFor(tenantId, lesson, concept, section, nodes.Count));

            if (conceptsInLesson % ConceptsPerCheck == 0)
                nodes.Add(CheckFor(tenantId, lesson, concept, nodes.Count));
        }

        return new ComposedCourse(course, modules, lessons, concepts, nodes);
    }

    private static IEnumerable<ScriptNode> ScriptFor(
        Guid tenantId, Lesson lesson, Concept concept, ExtractedSection section, int ordinalOffset)
    {
        var ordinal = ordinalOffset;

        foreach (var block in section.Blocks)
        {
            if (block.Kind == BlockKind.Code)
            {
                // Code gets its own beat, narrated rather than read out. Reading source aloud
                // is the thing that makes a tutor unbearable; saying what it does is teaching.
                yield return new ScriptNode
                {
                    TenantId = tenantId,
                    LessonId = lesson.Id,
                    Ordinal = ++ordinal,
                    Kind = ScriptNodeKind.CodePlayground,
                    ConceptKey = concept.Key,
                    Text = NarrationFor(concept.Title),
                    VisualKind = VisualKind.Code,
                    VisualPayload = block.Text,
                    VisualRef = $"code-{ordinal}",
                    SourceRef = block.SourceRef,
                    PauseAfterMs = 600,
                };
            }
        }

        var utterances = Utterances.Split(concept.Body);
        var listItems = section.Blocks.Where(block => block.Kind == BlockKind.ListItem).ToArray();

        for (var i = 0; i < utterances.Count; i++)
        {
            var isFirst = i == 0;
            yield return new ScriptNode
            {
                TenantId = tenantId,
                LessonId = lesson.Id,
                Ordinal = ++ordinal,
                Kind = ScriptNodeKind.Speech,
                ConceptKey = concept.Key,
                Text = utterances[i],
                // The opening beat of a concept states it, so it is the one that stays on
                // screen; the rest either list what is being enumerated or say it plainly.
                VisualKind = isFirst
                    ? (listItems.Length > 1 ? VisualKind.Steps : VisualKind.Statement)
                    : VisualKind.None,
                VisualPayload = isFirst
                    ? (listItems.Length > 1
                        ? string.Join('\n', listItems.Select(item => item.Text))
                        : concept.Title)
                    : null,
                VisualRef = isFirst ? $"concept-{concept.Key[..8]}" : null,
                SourceRef = concept.SourceRef,
                CarriesDefinition = isFirst,
                PauseAfterMs = 350,
            };
        }
    }

    private static ScriptNode CheckFor(Guid tenantId, Lesson lesson, Concept concept, int ordinal) =>
        new()
        {
            TenantId = tenantId,
            LessonId = lesson.Id,
            Ordinal = ordinal + 1,
            Kind = ScriptNodeKind.CheckForUnderstanding,
            ConceptKey = concept.Key,
            Text = $"Before we carry on — in your own words, what is {concept.Title.ToLowerInvariant()}?",
            VisualKind = VisualKind.None,
            SourceRef = concept.SourceRef,
            PauseAfterMs = 0,
        };

    /// <summary>
    /// A document with no headings is the common case, not an edge one — a pasted chapter, a
    /// transcript, an exported page. It still has to become a lesson, so paragraphs are grouped
    /// into sections by length rather than refused.
    /// </summary>
    private static IReadOnlyList<ExtractedSection> Sectioned(ExtractedDocument document)
    {
        if (document.Sections.Any(section => !string.IsNullOrWhiteSpace(section.Heading)))
            return document.Sections;

        var blocks = document.Sections.SelectMany(section => section.Blocks).ToArray();
        if (blocks.Length == 0) return [];

        var sections = new List<ExtractedSection>();
        var current = new List<ExtractedBlock>();
        var words = 0;

        foreach (var block in blocks)
        {
            current.Add(block);
            words += block.WordCount;

            if (words < Utterances.TargetWords * 3) continue;

            sections.Add(new ExtractedSection(FirstWordsOf(current[0].Text), 2, current.ToArray()));
            current.Clear();
            words = 0;
        }

        if (current.Count > 0)
            sections.Add(new ExtractedSection(FirstWordsOf(current[0].Text), 2, current.ToArray()));

        return sections;
    }

    private static string NarrationFor(string conceptTitle) =>
        $"Here is what that looks like in code. Follow the highlighted line as I talk through {conceptTitle.ToLowerInvariant()}.";

    private static string Titled(string? heading, string fallback) =>
        string.IsNullOrWhiteSpace(heading) ? fallback : heading.Trim();

    /// <summary>A usable title for something that never had one.</summary>
    private static string FirstWordsOf(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Untitled";
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(6);
        return string.Join(' ', words).TrimEnd('.', ',', ':', ';');
    }
}

public sealed record ComposedCourse(
    Course Course,
    IReadOnlyList<Module> Modules,
    IReadOnlyList<Lesson> Lessons,
    IReadOnlyList<Concept> Concepts,
    IReadOnlyList<ScriptNode> ScriptNodes)
{
    public IReadOnlyList<ScriptNode> ScriptFor(Guid lessonId) =>
        ScriptNodes.Where(node => node.LessonId == lessonId).OrderBy(node => node.Ordinal).ToArray();
}
