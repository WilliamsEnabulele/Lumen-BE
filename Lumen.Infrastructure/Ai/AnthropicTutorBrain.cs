using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Lumen.Domain.Canvas;
using Lumen.Domain.Teaching;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Ai;

/// <summary>
/// The tutor, talking.
///
/// One call per conversational turn. The model speaks and, in the same turn, reaches for the
/// display tools when a picture earns its place — so what goes on the canvas is a teaching
/// decision made in the moment rather than something precomputed from the document.
///
/// Refused tool calls are handed back to the model as tool errors rather than swallowed. That
/// is what lets it notice it aimed a highlight at a line that is not there and correct itself,
/// instead of talking about a line the student cannot see.
/// </summary>
public sealed class AnthropicTutorBrain(
    AnthropicClient client,
    AnthropicOptions options,
    ILogger<AnthropicTutorBrain> logger) : ITutorBrain
{
    /// <summary>
    /// How many times the model may draw, be answered, and speak again within one turn. Bounded
    /// because a student is waiting: a turn that takes four round trips has stopped being
    /// conversation.
    /// </summary>
    private const int MaxToolHops = 3;

    public string Name => $"anthropic:{options.TutorModel}";

    public async Task<TutorResponse> RespondAsync(
        TutorContext context,
        TutorIntent intent,
        string? studentSaid,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = new List<MessageParam>();

        foreach (var turn in context.RecentHistory)
        {
            messages.Add(new MessageParam
            {
                Role = turn.Speaker == Speaker.Tutor ? Role.Assistant : Role.User,
                Content = turn.Text,
            });
        }

        messages.Add(new MessageParam
        {
            Role = Role.User,
            Content = string.IsNullOrWhiteSpace(studentSaid)
                // Nothing was said: this is the tutor carrying on, so the nudge is an instruction
                // rather than words the student would ever see in the transcript.
                ? "[continue teaching]"
                : studentSaid,
        });

        var spoken = new StringBuilder();
        var drew = new List<CanvasCommand>();
        var refusals = new List<string>();
        var canvas = context.Canvas;
        var complete = false;

        for (var hop = 0; hop < MaxToolHops; hop++)
        {
            // Three system blocks, split at their stability boundaries, with a cache breakpoint
            // on the first two. Tools render before the system prompt, so the first breakpoint
            // covers the tool definitions as well — the largest fixed cost in every request.
            // Only the third block changes per turn, which is why it is last and unmarked.
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = options.TutorModel,
                MaxTokens = options.TutorMaxTokens,
                System = new List<TextBlockParam>
                {
                    new() { Text = TutorPrompt.Rules, CacheControl = new CacheControlEphemeral() },
                    new() { Text = TutorPrompt.Material(context), CacheControl = new CacheControlEphemeral() },
                    new() { Text = TutorPrompt.Now(context with { Canvas = canvas }, intent) },
                },
                Messages = messages,
                Tools = BuildTools(),
            }, cancellationToken);

            Record(response.Usage);

            List<ContentBlockParam> assistantContent = [];
            List<ContentBlockParam> toolResults = [];

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var text))
                {
                    spoken.Append(text.Text);
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    assistantContent.Add(new ToolUseBlockParam
                    {
                        ID = toolUse.ID,
                        Name = toolUse.Name,
                        Input = toolUse.Input,
                    });

                    if (toolUse.Name == TutorControlTools.ConceptTaught)
                    {
                        complete = true;
                        toolResults.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = "Noted." });
                        continue;
                    }

                    var input = JsonSerializer.SerializeToElement(toolUse.Input);
                    var result = CanvasToolParser.Parse(toolUse.Name, input, canvas);

                    if (result.Drawn)
                    {
                        drew.Add(result.Command!);
                        canvas = canvas.Apply(result.Command!);
                        toolResults.Add(new ToolResultBlockParam
                        {
                            ToolUseID = toolUse.ID,
                            Content = "On the canvas.",
                        });
                    }
                    else
                    {
                        refusals.Add($"{toolUse.Name}: {result.Refusal}");
                        logger.LogInformation("Canvas call refused — {Tool}: {Reason}", toolUse.Name, result.Refusal);
                        toolResults.Add(new ToolResultBlockParam
                        {
                            ToolUseID = toolUse.ID,
                            Content = result.Refusal ?? "That could not be drawn.",
                            IsError = true,
                        });
                    }
                }
            }

            if (toolResults.Count == 0) break;

            messages = [
                .. messages,
                new MessageParam { Role = Role.Assistant, Content = assistantContent },
                new MessageParam { Role = Role.User, Content = toolResults },
            ];
        }

        return new TutorResponse(spoken.ToString().Trim(), drew, complete, refusals);
    }

    /// <summary>
    /// Logs what the cache actually did.
    ///
    /// Caching fails silently: a single changed byte anywhere in the prefix turns every request
    /// into a full-price write and nothing in the response says so. Reads staying at zero across
    /// a lesson is the signal that something volatile has crept above a breakpoint.
    /// </summary>
    private void Record(Usage usage)
    {
        logger.LogDebug(
            "Turn billed {Fresh} fresh, {Written} written to cache, {Read} read from cache, {Output} out.",
            usage.InputTokens, usage.CacheCreationInputTokens, usage.CacheReadInputTokens, usage.OutputTokens);
    }

    private static List<ToolUnion> BuildTools()
    {
        var tools = new List<ToolUnion>();

        foreach (var definition in CanvasTools.All)
        {
            tools.Add(new Tool
            {
                Name = definition.Name,
                Description = definition.Description,
                InputSchema = new()
                {
                    Properties = JsonSchemas.Properties(definition.JsonSchema),
                    Required = JsonSchemas.Required(definition.JsonSchema),
                },
            });
        }

        tools.Add(new Tool
        {
            Name = TutorControlTools.ConceptTaught,
            Description = TutorControlTools.ConceptTaughtDescription,
            InputSchema = new()
            {
                Properties = JsonSchemas.Properties(TutorControlTools.ConceptTaughtSchema),
                Required = JsonSchemas.Required(TutorControlTools.ConceptTaughtSchema),
            },
        });

        return tools;
    }
}
