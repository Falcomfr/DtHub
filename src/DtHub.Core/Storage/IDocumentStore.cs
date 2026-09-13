namespace DtHub.Core.Storage;

/// <summary>
/// Persistence of a configuration document. An unreadable file must
/// never prevent the application from starting: the implementation
/// sets the faulty file aside and starts over from a valid document.
/// </summary>
public interface IDocumentStore<T>
    where T : class, new()
{
    /// <summary>Path of the managed file, for diagnostics.</summary>
    string FilePath { get; }

    /// <summary>
    /// Loads the document. Returns a fresh document if the file is
    /// missing, empty or unreadable. Does not throw on invalid
    /// content.
    /// </summary>
    Task<T> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the document atomically.</summary>
    Task SaveAsync(T document, CancellationToken cancellationToken = default);

    /// <summary>
    /// The document as it would be written to disk.
    ///
    /// Here and not on the caller's side: it is the store that knows
    /// the format, and a second set of options elsewhere would
    /// eventually diverge from the first.
    /// </summary>
    string Serialize(T document);

    /// <summary>
    /// The document carried by this text, or <c>null</c> if none is
    /// found there. Does not throw.
    /// </summary>
    T? Deserialize(string json);
}
