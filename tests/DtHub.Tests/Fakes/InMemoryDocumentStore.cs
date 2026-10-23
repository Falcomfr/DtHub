using System.Text.Json;

using DtHub.Core.Storage;

namespace DtHub.Tests.Fakes;

/// <summary>
/// In-memory storage, to check a service without touching disk.
/// </summary>
public sealed class InMemoryDocumentStore<T> : IDocumentStore<T>
    where T : class, new()
{
    private T? _document;

    public string FilePath => "(mémoire)";

    /// <summary>
    /// Number of writes, to check that nothing gets written needlessly.
    /// </summary>
    public int Writes { get; private set; }

    /// <summary>As a file held by another program: reads hand out a fresh document, writes are dropped.</summary>
    public bool IsUnread { get; set; }

    public Task<T> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsUnread ? new T() : _document ?? new T());

    public string Serialize(T document) => JsonSerializer.Serialize(document);

    public T? Deserialize(string json) => JsonSerializer.Deserialize<T>(json);

    public Task SaveAsync(T document, CancellationToken cancellationToken = default)
    {
        // The real store refuses to write the default document over a file it could not read.
        if (IsUnread)
        {
            return Task.CompletedTask;
        }

        _document = document;
        Writes++;

        return Task.CompletedTask;
    }
}
