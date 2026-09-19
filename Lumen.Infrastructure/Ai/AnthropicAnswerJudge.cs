using Anthropic;
using Anthropic.Models.Messages;
using Lumen.Domain.Assessment;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Ai;

/// <summary>
/// Marks a spoken answer.
///
/// A short, cheap call with a fixed system prompt — the marking rules never change, so they sit
/// behind a cache breakpoint the same way the tutor's rules do.
///
/// Every failure lands on "no answer" rather than a guess. A judge that cannot reach the model,
/// or returns something unparseable, must not cost a student a mark or trigger a reteach they
/// did not earn.
/// </summary>
public sealed class AnthropicAnswerJudge(
    AnthropicClient client,
    AnthropicOptions options,
    ILogger<AnthropicAnswerJudge> logger) : IAnswerJudge
{
    public async Task<AnswerJudgement> JudgeAsync(
        string conceptTitle,
        string sourceExcerpt,
        string question,
        string studentAnswer,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnswer)) return AnswerJudgement.Unknown;

        try
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = options.JudgeModel,
                MaxTokens = options.JudgeMaxTokens,
                System = new List<TextBlockParam>
                {
                    new() { Text = JudgePrompt.System, CacheControl = new CacheControlEphemeral() },
                },
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = JudgePrompt.User(conceptTitle, sourceExcerpt, question, studentAnswer),
                    },
                ],
                OutputConfig = new OutputConfig
                {
                    Format = new JsonOutputFormat { Schema = JsonSchemas.Whole(JudgePrompt.VerdictSchema) },
                },
            }, cancellationToken);

            var json = string.Concat(response.Content
                .Select(block => block.TryPickText(out var text) ? text.Text : string.Empty));

            return JudgePrompt.ReadJudgement(json);
        }
        catch (Exception exception)
        {
            // Failing to mark must never read as failing the question.
            logger.LogWarning(exception, "Could not judge an answer for {Concept}; recording no answer.", conceptTitle);
            return AnswerJudgement.Unknown;
        }
    }

}

/// <summary>
/// The judge with no model behind it.
///
/// It marks nothing, which leaves every estimate exactly where it was — the honest outcome when
/// there is nothing here capable of reading an answer. A fallback that guessed would quietly
/// reteach people who were right.
/// </summary>
public sealed class UnjudgedAnswers : IAnswerJudge
{
    public Task<AnswerJudgement> JudgeAsync(
        string conceptTitle,
        string sourceExcerpt,
        string question,
        string studentAnswer,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AnswerJudgement.Unknown);
}
