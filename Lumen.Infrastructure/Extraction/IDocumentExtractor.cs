using Lumen.Domain.Ingestion;

namespace Lumen.Infrastructure.Extraction;

public interface IDocumentExtractor
{
    /// <summary>File extensions this extractor handles, lowercase and with the dot.</summary>
    IReadOnlyCollection<string> Extensions { get; }

    ExtractedDocument Extract(Stream content, string fileName);
}

/// <summary>
/// Picks an extractor by file extension, and refuses clearly when there is none.
///
/// Refusing by name matters more than it looks: a student who uploads something unreadable and
/// gets a lesson built from nothing has been failed silently, which is worse than being told
/// the format is not supported yet. The same principle runs one level deeper inside
/// <see cref="PdfExtractor"/>, which refuses a PDF it can open but cannot read.
/// </summary>
public sealed class DocumentExtractors(IEnumerable<IDocumentExtractor> extractors)
{
    private readonly IReadOnlyList<IDocumentExtractor> _extractors = extractors.ToArray();

    public IReadOnlyCollection<string> SupportedExtensions =>
        _extractors.SelectMany(extractor => extractor.Extensions).Distinct().Order().ToArray();

    public IDocumentExtractor? For(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return _extractors.FirstOrDefault(extractor => extractor.Extensions.Contains(extension));
    }
}

/// <summary>Thrown when a format is not supported. Carries what to do instead.</summary>
public sealed class UnsupportedFormatException(string extension, IReadOnlyCollection<string> supported)
    : Exception(BuildMessage(extension, supported))
{
    private static string BuildMessage(string extension, IReadOnlyCollection<string> supported) =>
        string.IsNullOrEmpty(extension)
            ? $"That file has no extension, so there is no way to tell how to read it. Supported: {string.Join(", ", supported)}."
            : $"{extension} is not supported yet. Supported: {string.Join(", ", supported)}.";
}
