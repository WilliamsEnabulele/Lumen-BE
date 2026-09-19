using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Lumen.Domain.Ingestion;
using Lumen.Domain.Teaching;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Ai;

/// <summary>
/// Reads the document and decides what it teaches.
///
/// Structured output rather than "please reply in JSON": the plan has to parse, and a malformed
/// reply here fails an upload the student is waiting on. The schema is the contract.
/// </summary>
public sealed class AnthropicLessonAuthor(
    AnthropicClient client,
    AnthropicOptions options,
    ILogger<AnthropicLessonAuthor> logger) : ILessonAuthor
{
    public string Name => $"anthropic:{options.AuthorModel}";

    public async Task<LessonPlan> AuthorAsync(
        ExtractedDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = options.AuthorModel,
            MaxTokens = options.AuthorMaxTokens,
            System = AuthorPrompt.System,
            Messages = [new() { Role = Role.User, Content = AuthorPrompt.User(document) }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = JsonSchemas.Whole(AuthorPrompt.PlanSchema) },
            },
        }, cancellationToken);

        var json = string.Concat(response.Content
            .Select(block => block.TryPickText(out var text) ? text.Text : string.Empty));

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("The lesson author returned nothing to parse.");

        logger.LogInformation("Authored a plan from {Words} words of {Title}.", document.WordCount, document.Title);
        return LessonPlanReader.Read(json, document.Title);
    }
}
