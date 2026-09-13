namespace DtHub.Tests.Fakes;

/// <summary>
/// Reports progress on the thread that raises it, without going
/// through a synchronization context.
///
/// <see cref="Progress{T}"/> posts its callbacks, which forces a
/// test to wait for them to arrive. The contract is
/// <see cref="IProgress{T}"/>: a direct implementation makes the
/// report immediate, so the test deterministic.
/// </summary>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;

    public SyncProgress(Action<T> report) => _report = report;

    public void Report(T value) => _report(value);
}
