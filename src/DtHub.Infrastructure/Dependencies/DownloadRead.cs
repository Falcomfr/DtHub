using DtHub.Core.Localization;

namespace DtHub.Infrastructure.Dependencies;

/// <summary>
/// One read of a download, bounded. Once the headers are in, HttpClient's
/// timeout no longer applies, and a connection gone quiet, the PC asleep or
/// switched to another network, left a download waiting forever with its
/// row spinning and no way to retry.
/// </summary>
public static class DownloadRead
{
    /// <summary>Long enough for a slow link, short of looking dead.</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Reads the next block, or throws an <see cref="IOException" /> when
    /// nothing came for <paramref name="stall" />: the error the callers
    /// already report, rather than a cancellation nobody asked for.
    /// </summary>
    public static async ValueTask<int> NextAsync(
        Stream source,
        Memory<byte> buffer,
        TimeSpan stall,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(stall);

        try
        {
            return await source.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException(Strings.Format("DownloadStalled", (int)stall.TotalSeconds));
        }
    }
}
