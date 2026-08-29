using DtHub.Core.Apps;

namespace DtHub.Tests.Fakes;

/// <summary>Fournisseur de noms simulé, capable de tomber en panne à la demande.</summary>
public sealed class FakeAppLabelProvider : IAppLabelProvider
{
    private readonly IReadOnlyDictionary<string, string> _labels;
    private readonly bool _throwOnUse;

    public FakeAppLabelProvider(
        IReadOnlyDictionary<string, string>? labels = null,
        bool throwOnUse = false)
    {
        _labels = labels ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _throwOnUse = throwOnUse;
    }

    public Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(
        string serial,
        CancellationToken cancellationToken = default) =>
        _throwOnUse
            ? Task.FromException<IReadOnlyDictionary<string, string>>(
                new InvalidOperationException("Fournisseur de noms indisponible."))
            : Task.FromResult(_labels);
}
