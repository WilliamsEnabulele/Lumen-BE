using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumen.Infrastructure.Storage;

/// <summary>
/// The shared half of every file-backed store here: how a record is written so a crash cannot
/// corrupt it, and how a folder of them is read back at startup.
///
/// Files rather than a database, still deliberately. What this has to get right is durability
/// and honest recovery, not queries — nothing yet asks a question that a dictionary cannot
/// answer. When something does, the interfaces above these are what changes, and the callers
/// are not.
/// </summary>
internal static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Written aside and moved into place. A process that dies mid-write leaves the previous
    /// version intact rather than a half-parsed one — which matters most for exactly the file
    /// being written most often, the session of a lesson in progress.
    /// </summary>
    public static void Write<T>(string path, T value)
    {
        var staging = path + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(value, Options));
        File.Move(staging, path, overwrite: true);
    }

    /// <summary>
    /// Everything readable in a folder. A file that will not parse is skipped rather than
    /// thrown, because one unreadable record must not stop the server starting — it reports as
    /// missing, which is the truth about it, and the rest of the students keep their progress.
    /// </summary>
    public static IEnumerable<T> ReadAll<T>(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            T? value;
            try
            {
                value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            }
            catch (JsonException)
            {
                continue;
            }

            if (value is not null) yield return value;
        }
    }
}
