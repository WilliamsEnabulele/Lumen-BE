using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Lumen.Infrastructure.Ai;

/// <summary>
/// Rewrites a JSON Schema into the dialect Gemini's structured output accepts.
///
/// The schemas in the domain are written as JSON Schema and are the contract the plan has to
/// satisfy. Gemini takes an OpenAPI subset instead, and the differences are exactly the parts
/// this contract leans on: it rejects <c>additionalProperties</c> outright, and it will not
/// take a union type, so <c>["string", "null"]</c> has to become a string that is nullable.
///
/// Translating rather than keeping a second copy of every schema is the whole point. Two
/// schemas drift, and the drift shows up as a plan that parsed on one provider and fails on
/// the other — at upload time, with a student waiting.
/// </summary>
public static class GeminiSchema
{
    public static string From(string jsonSchema)
    {
        using var document = JsonDocument.Parse(jsonSchema);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(document.RootElement, writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(element, writer);
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) Write(item, writer);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteObject(JsonElement element, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        var nullable = false;

        foreach (var property in element.EnumerateObject())
        {
            // Not in the dialect. Sending it is a 400, not a field quietly ignored.
            if (property.NameEquals("additionalProperties")) continue;

            if (property.NameEquals("type") && property.Value.ValueKind == JsonValueKind.Array)
            {
                writer.WriteString("type", Collapse(property.Value, ref nullable));
                continue;
            }

            writer.WritePropertyName(property.Name);
            Write(property.Value, writer);
        }

        // Written last so it cannot be overwritten by a "nullable" the source already carried.
        if (nullable) writer.WriteBoolean("nullable", true);

        writer.WriteEndObject();
    }

    /// <summary>
    /// A union type becomes the one concrete type it names, plus nullability. A schema that
    /// unions two real types is beyond what either dialect should be asked to express, so the
    /// first wins rather than the call failing on something nobody will read.
    /// </summary>
    private static string Collapse(JsonElement types, ref bool nullable)
    {
        string? concrete = null;

        foreach (var entry in types.EnumerateArray())
        {
            var name = entry.GetString();
            if (name is null) continue;

            if (name == "null") nullable = true;
            else concrete ??= name;
        }

        return concrete ?? "string";
    }
}
