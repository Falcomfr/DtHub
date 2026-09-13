using System.Text.Json;

using DtHub.Core.Storage;

namespace DtHub.Tests.Fakes;

/// <summary>Stockage en mémoire, pour vérifier un service sans toucher au disque.</summary>
public sealed class InMemoryDocumentStore<T> : IDocumentStore<T>
    where T : class, new()
{
    private T? _document;

    public string FilePath => "(mémoire)";

    /// <summary>Nombre d'écritures, pour vérifier qu'on n'écrit pas pour rien.</summary>
    public int Writes { get; private set; }

    public Task<T> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_document ?? new T());

    public string Serialize(T document) => JsonSerializer.Serialize(document);

    public T? Deserialize(string json) => JsonSerializer.Deserialize<T>(json);

    public Task SaveAsync(T document, CancellationToken cancellationToken = default)
    {
        _document = document;
        Writes++;

        return Task.CompletedTask;
    }
}
