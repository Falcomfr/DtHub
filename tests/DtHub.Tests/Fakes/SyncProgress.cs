namespace DtHub.Tests.Fakes;

/// <summary>
/// Rapporte l'avancement sur le fil qui le signale, sans passer par un contexte
/// de synchronisation.
///
/// <see cref="Progress{T}"/> poste ses rappels, ce qui oblige une épreuve à
/// attendre qu'ils arrivent. Le contrat est <see cref="IProgress{T}"/> : une
/// implémentation directe rend le rapport immédiat, donc l'épreuve
/// déterministe.
/// </summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;

    public SyncProgress(Action<T> report) => _report = report;

    public void Report(T value) => _report(value);
}
