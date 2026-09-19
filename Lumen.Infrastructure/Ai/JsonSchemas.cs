using System.Text.Json;

namespace Lumen.Infrastructure.Ai;

/// <summary>
/// The schemas are authored as JSON text in the domain so they read as schemas. The SDK wants
/// them as property dictionaries, so this is the seam between the two.
/// </summary>
internal static class JsonSchemas
{
    public static Dictionary<string, JsonElement> Properties(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        var properties = new Dictionary<string, JsonElement>();

        if (document.RootElement.TryGetProperty("properties", out var declared))
        {
            foreach (var property in declared.EnumerateObject())
                properties[property.Name] = property.Value.Clone();
        }

        return properties;
    }

    public static List<string> Required(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        var required = new List<string>();

        if (document.RootElement.TryGetProperty("required", out var declared)
            && declared.ValueKind == JsonValueKind.Array)
        {
            required.AddRange(declared.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!));
        }

        return required;
    }

    /// <summary>The whole schema, as the top-level map a structured-output format wants.</summary>
    public static Dictionary<string, JsonElement> Whole(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}
