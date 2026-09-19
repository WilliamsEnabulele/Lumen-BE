using System.Text.Json;
using Lumen.Domain.Teaching;
using Lumen.Infrastructure.Ai;

namespace Lumen.Tests;

/// <summary>
/// Translating the domain's schemas into the dialect Gemini accepts.
///
/// Worth testing rather than eyeballing because every difference between the two dialects is
/// silent in one direction: a schema Gemini rejects fails the upload with a 400 that names a
/// field, and a schema it accepts but misreads produces a plan that parses and is wrong.
/// </summary>
public class GeminiSchemaTests
{
    private static JsonElement Translate(string schema) =>
        JsonDocument.Parse(GeminiSchema.From(schema)).RootElement.Clone();

    [Fact]
    public void The_real_plan_schema_comes_out_clean()
    {
        var translated = GeminiSchema.From(AuthorPrompt.PlanSchema);

        Assert.DoesNotContain("additionalProperties", translated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"null\"", translated, StringComparison.Ordinal);

        // And is still the same schema: the contract has to survive the translation.
        var root = JsonDocument.Parse(translated).RootElement;
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("properties").TryGetProperty("lessons", out _));
        Assert.Equal(3, root.GetProperty("required").GetArrayLength());
    }

    [Fact]
    public void A_nullable_union_becomes_a_type_that_is_nullable()
    {
        var translated = Translate("""{ "type": ["string", "null"] }""");

        Assert.Equal("string", translated.GetProperty("type").GetString());
        Assert.True(translated.GetProperty("nullable").GetBoolean());
    }

    [Fact]
    public void A_plain_type_is_left_exactly_as_it_was()
    {
        var translated = Translate("""{ "type": "string", "description": "a title" }""");

        Assert.Equal("string", translated.GetProperty("type").GetString());
        Assert.Equal("a title", translated.GetProperty("description").GetString());
        Assert.False(translated.TryGetProperty("nullable", out _));
    }

    [Fact]
    public void Nullability_is_found_however_deep_it_is_nested()
    {
        var translated = Translate("""
            {
              "type": "object",
              "properties": {
                "lessons": {
                  "type": "array",
                  "items": { "type": "object", "properties": { "hint": { "type": ["string", "null"] } } }
                }
              }
            }
            """);

        var hint = translated
            .GetProperty("properties").GetProperty("lessons")
            .GetProperty("items")
            .GetProperty("properties").GetProperty("hint");

        Assert.Equal("string", hint.GetProperty("type").GetString());
        Assert.True(hint.GetProperty("nullable").GetBoolean());
    }

    [Fact]
    public void Constraints_worth_keeping_are_kept()
    {
        // minItems is the difference between a plan with lessons and a plan with none.
        var translated = Translate("""{ "type": "array", "minItems": 1, "items": { "type": "string" } }""");

        Assert.Equal(1, translated.GetProperty("minItems").GetInt32());
    }
}

/// <summary>
/// Which provider ends up serving each role. The rule being protected is that a choice nobody
/// can honour fails loudly, because the alternative is a cheaper bill and a tutor that stopped
/// teaching, with nothing saying which happened.
/// </summary>
public class AiWiringTests
{
    [Fact]
    public void A_keyless_install_falls_back_so_the_upload_path_still_runs()
    {
        var resolved = AiWiring.Resolve(
            AiRole.Tutor, AiProvider.Anthropic, chosenExplicitly: false,
            anthropicReady: false, googleReady: false);

        Assert.Equal(AiProvider.None, resolved);
    }

    [Fact]
    public void A_provider_that_was_actually_chosen_never_falls_back_quietly()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => AiWiring.Resolve(
            AiRole.Author, AiProvider.Google, chosenExplicitly: true,
            anthropicReady: true, googleReady: false));

        Assert.Contains("GOOGLE_API_KEY", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_role_no_provider_implements_is_refused_rather_than_half_wired()
    {
        // Gemini reads documents here; it does not yet teach or mark. Offering it as a setting
        // that silently does nothing would be worse than not offering it.
        var failure = Assert.Throws<InvalidOperationException>(() => AiWiring.Resolve(
            AiRole.Tutor, AiProvider.Google, chosenExplicitly: true,
            anthropicReady: true, googleReady: true));

        Assert.Contains("no Google implementation", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_roles_are_chosen_one_at_a_time()
    {
        // The point of the whole seam: a cheap model reads the document, a good one teaches.
        Assert.Equal(AiProvider.Google, AiWiring.Resolve(
            AiRole.Author, AiProvider.Google, true, anthropicReady: true, googleReady: true));

        Assert.Equal(AiProvider.Anthropic, AiWiring.Resolve(
            AiRole.Tutor, AiProvider.Anthropic, true, anthropicReady: true, googleReady: true));
    }

    [Fact]
    public void Every_role_has_a_provider_that_can_serve_it()
    {
        foreach (var role in Enum.GetValues<AiRole>())
        {
            Assert.True(AiWiring.Supports(AiProvider.Anthropic, role), $"{role} has no Anthropic path");
            Assert.True(AiWiring.Supports(AiProvider.None, role), $"{role} has no fallback");
        }
    }

    [Fact]
    public void Routing_defaults_to_what_was_already_being_used()
    {
        var routing = new AiRouting();

        Assert.Equal(AiProvider.Anthropic, routing.Author);
        Assert.Equal(AiProvider.Anthropic, routing.Tutor);
        Assert.Equal(AiProvider.Anthropic, routing.Judge);
    }
}
