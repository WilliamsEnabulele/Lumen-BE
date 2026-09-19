using System.Text;
using Lumen.Domain.Ingestion;

namespace Lumen.Domain.Teaching;

/// <summary>
/// What the model is told when it reads a document and decides what it teaches.
///
/// The instruction that matters most is the one about not inventing: a model asked to turn a
/// document into a course will happily round it out with material the document does not
/// contain, and every one of those additions is something no instructor approved and no source
/// supports.
/// </summary>
public static class AuthorPrompt
{
    /// <summary>
    /// How much of the document to send. Large enough for a chapter, bounded so a whole textbook
    /// is chunked rather than silently truncated.
    /// </summary>
    public const int MaxCharacters = 120_000;

    public const string System = """
        You are turning a document into a course that will be taught out loud by a voice tutor.

        Decide what the document actually teaches and lay it out as lessons and concepts. You are
        writing a plan, not a script — say what each concept is for and what the student should be
        able to do, never the words the tutor will say. The tutor phrases everything live.

        Rules that matter more than coverage:

        - Teach only what is in the document. Do not add background, context or examples it does not
          contain, however helpful they would be. Anything you add is something no author wrote and
          no reviewer approved.
        - If the document is thin, produce a short course. A thin document honestly turned into two
          concepts is worth more than a padded one turned into ten.
        - Order concepts so nothing depends on something taught later. List prerequisites by the exact
          title of the earlier concept.
        - Every concept carries the passage it came from, verbatim, in source_excerpt. The tutor is
          grounded in that text while teaching, so it must contain what the concept needs — not a
          paraphrase, and not the whole document.
        - Suggest a visual only where one genuinely helps: structure, sequence, code, or the shape of
          some numbers. Leave it null otherwise. Most concepts do not need one.
        """;

    public static string User(ExtractedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var text = new StringBuilder();
        text.AppendLine($"Document title: {document.Title}");
        text.AppendLine();

        foreach (var section in document.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading))
                text.AppendLine($"{new string('#', Math.Clamp(section.Level, 1, 6))} {section.Heading}");

            foreach (var block in section.Blocks)
            {
                text.AppendLine(block.Kind == BlockKind.Code
                    ? $"[code @ {block.SourceRef}]\n{block.Text}"
                    : $"[{block.SourceRef}] {block.Text}");
            }

            text.AppendLine();
        }

        var body = text.ToString();
        return body.Length <= MaxCharacters ? body : body[..MaxCharacters];
    }

    /// <summary>The shape the plan must come back in, enforced by structured output.</summary>
    public const string PlanSchema = """
        {
          "type": "object",
          "properties": {
            "course_title": { "type": "string" },
            "summary": { "type": "string", "description": "One sentence on what this course covers." },
            "lessons": {
              "type": "array",
              "minItems": 1,
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "objective": { "type": "string", "description": "What the student can do afterwards." },
                  "concepts": {
                    "type": "array",
                    "minItems": 1,
                    "items": {
                      "type": "object",
                      "properties": {
                        "title": { "type": "string" },
                        "teaching_intent": { "type": "string" },
                        "prerequisites": { "type": "array", "items": { "type": "string" } },
                        "source_ref": { "type": "string" },
                        "source_excerpt": { "type": "string" },
                        "visual_hint": { "type": ["string", "null"] }
                      },
                      "required": ["title", "teaching_intent", "prerequisites", "source_ref", "source_excerpt", "visual_hint"],
                      "additionalProperties": false
                    }
                  }
                },
                "required": ["title", "objective", "concepts"],
                "additionalProperties": false
              }
            }
          },
          "required": ["course_title", "summary", "lessons"],
          "additionalProperties": false
        }
        """;
}
