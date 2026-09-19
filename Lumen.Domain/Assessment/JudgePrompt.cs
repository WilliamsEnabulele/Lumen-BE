using System.Text.Json;

namespace Lumen.Domain.Assessment;

/// <summary>
/// What the judge is told before it marks an answer.
///
/// The whole prompt bends in one direction: generous about how it was said, strict about what
/// was said. These are spoken answers in a conversation — half-sentences, fillers, the student
/// talking themselves into the idea as they go. Marking on phrasing would fail the students who
/// understand it and reward the ones who memorised the wording, which is the opposite of what
/// the estimate is for.
/// </summary>
public static class JudgePrompt
{
    public const string System = """
        You are marking a single spoken answer a student gave a tutor, out loud, mid-lesson.

        Judge the substance, never the phrasing. These answers are spoken, so they ramble, start
        over, use their own words and leave things implied. A student who says "it like, doubles
        every time" has understood something a student reciting the textbook sentence may not
        have. Mark the first one correct.

        The four verdicts:

        - correct: they have the idea. Wording, order and completeness of expression do not
          matter if the substance is right.
        - partial: some of it, with a real gap — not a phrasing gap.
        - incorrect: they believe something that is not so, or have missed the point entirely.
        - no_answer: they did not attempt it. A question back, "hang on", thinking aloud with no
          claim in it, a request to repeat, or silence. This is not failure and must not be
          marked as one — it carries no information about what they know.

        When the answer is wrong or partial, say in one short phrase what they appear to believe
        instead, in plain language. That phrase is shown to an instructor, so write it about the
        idea rather than about the student. Leave it null when they were right or said nothing.

        Judge only against the material you are given. If the answer is right about the subject
        but goes beyond what the material covers, that is still correct.
        """;

    public const string VerdictSchema = """
        {
          "type": "object",
          "properties": {
            "verdict": { "type": "string", "enum": ["correct", "partial", "incorrect", "no_answer"] },
            "misconception": { "type": ["string", "null"] }
          },
          "required": ["verdict", "misconception"],
          "additionalProperties": false
        }
        """;

    public static string User(string conceptTitle, string sourceExcerpt, string question, string answer) =>
        $"""
        Concept: {conceptTitle}

        The material it is taught from:
        ```
        {sourceExcerpt}
        ```

        The tutor asked:
        {question}

        The student said:
        {answer}
        """;

    /// <summary>
    /// Reads the marked reply.
    ///
    /// Every unreadable case lands on "no answer", which moves no estimate and triggers no
    /// reteach. A parser that guessed would cost a student a mark for a model's formatting.
    /// </summary>
    public static AnswerJudgement ReadJudgement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return AnswerJudgement.Unknown;

        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;

            var verdict = Read(
                root.TryGetProperty("verdict", out var raw) && raw.ValueKind == JsonValueKind.String
                    ? raw.GetString()
                    : null);

            var misconception =
                root.TryGetProperty("misconception", out var note) && note.ValueKind == JsonValueKind.String
                    ? note.GetString()
                    : null;

            return new AnswerJudgement(verdict, string.IsNullOrWhiteSpace(misconception) ? null : misconception);
        }
        catch (JsonException)
        {
            return AnswerJudgement.Unknown;
        }
    }

    /// <summary>Maps the model's word to a verdict, defaulting to the one that changes nothing.</summary>
    public static Verdict Read(string? verdict) => verdict?.Trim().ToLowerInvariant() switch
    {
        "correct" => Verdict.Correct,
        "partial" => Verdict.Partial,
        "incorrect" => Verdict.Incorrect,
        // Anything unrecognised is treated as no answer rather than guessed at: an unparsed
        // reply must not cost a student a mark.
        _ => Verdict.NoAnswer
    };
}
