namespace Stove.Net.Http;

/// <summary>
/// Represents a part in a multipart form data request.
/// Matches Kotlin Stove's StoveMultiPartContent variants.
/// </summary>
public abstract record StoveMultiPartContent(string Name)
{
    /// <summary>A text field in the multipart form.</summary>
    public sealed record Text(string Name, string Value) : StoveMultiPartContent(Name);

    /// <summary>A binary blob with a filename.</summary>
    public sealed record Binary(string Name, byte[] Data, string FileName) : StoveMultiPartContent(Name);

    /// <summary>A file stream with optional content type.</summary>
    public sealed record File(string Name, Stream Stream, string FileName, string? ContentType = null)
        : StoveMultiPartContent(Name);
}
