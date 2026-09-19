using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumen.Domain.Ingestion;
using Lumen.Domain.Teaching;
using Microsoft.Extensions.Logging;

namespace Lumen.Infrastructure.Ai;

/// <summary>
/// Reads the document and decides what it teaches, on Gemini.
///
/// The role this is worth doing cheaply. Ingestion is one large call per upload, paid once,
/// against a model with room for a whole chapter — and, unlike teaching, its output is not
/// handed to a student. It goes through <see cref="LessonPlanReader"/> and the plan validator
/// first, so a weaker model's mistakes surface as a rejected plan rather than as a bad lesson.
///
/// Written against the REST endpoint rather than a client library on purpose: one POST and one
/// response shape is less surface than a dependency, and the prompt and the schema are shared
/// with the Anthropic author rather than restated here.
/// </summary>
public sealed class GeminiLessonAuthor(
    HttpClient http,
    GoogleOptions options,
    ILogger<GeminiLessonAuthor> logger) : ILessonAuthor
{
    public string Name => $"google:{options.AuthorModel}";

    public async Task<LessonPlan> AuthorAsync(
        ExtractedDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var body = new JsonObject
        {
            ["systemInstruction"] = Parts(AuthorPrompt.System),
            ["contents"] = new JsonArray(Turn("user", AuthorPrompt.User(document))),
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = options.AuthorMaxTokens,
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = JsonNode.Parse(GeminiSchema.From(AuthorPrompt.PlanSchema)),
            },
        };

        var url = $"{options.Endpoint.TrimEnd('/')}/models/{options.AuthorModel}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        // The key travels as a header rather than a query parameter, because query strings reach
        // access logs and proxies and a key in one of those has to be rotated.
        request.Headers.TryAddWithoutValidation("x-goog-api-key", options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Gemini refused the lesson plan request ({(int)response.StatusCode}): {Trim(payload)}");

        var json = ReadPlan(payload);

        logger.LogInformation(
            "Authored a plan from {Words} words of {Title} on {Model}.",
            document.WordCount, document.Title, options.AuthorModel);

        return LessonPlanReader.Read(json, document.Title);
    }

    private static JsonObject Parts(string text) =>
        new() { ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) };

    private static JsonObject Turn(string role, string text)
    {
        var turn = Parts(text);
        turn["role"] = role;
        return turn;
    }

    /// <summary>
    /// Pulls the plan out of a response, and refuses the ones that only look like plans.
    ///
    /// A generation cut off at the token limit returns valid JSON describing a truncated plan,
    /// and the parser downstream reports it as a malformed plan — which sends whoever is
    /// debugging it looking at the schema instead of at the limit. Named here instead.
    /// </summary>
    private static string ReadPlan(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Gemini returned no candidates: {Trim(payload)}");
        }

        var candidate = candidates[0];

        if (candidate.TryGetProperty("finishReason", out var finish)
            && finish.GetString() is { } reason
            && reason is not ("STOP" or "MAX_TOKENS"))
        {
            throw new InvalidOperationException($"Gemini stopped early: {reason}.");
        }

        if (candidate.TryGetProperty("finishReason", out var limit) && limit.GetString() == "MAX_TOKENS")
            throw new InvalidOperationException(
                "Gemini hit the output limit before finishing the plan. Raise Ai:Google:AuthorMaxTokens "
                + "or give it a shorter document.");

        var text = new StringBuilder();

        if (candidate.TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var value) && value.GetString() is { } chunk)
                    text.Append(chunk);
            }
        }

        if (text.Length == 0)
            throw new InvalidOperationException("The lesson author returned nothing to parse.");

        return text.ToString();
    }

    /// <summary>Errors reach logs, and a whole model response in a log line helps nobody.</summary>
    private static string Trim(string payload) =>
        payload.Length <= 400 ? payload : payload[..400] + "…";
}
