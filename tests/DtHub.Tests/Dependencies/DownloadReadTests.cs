using DtHub.Infrastructure.Dependencies;

namespace DtHub.Tests.Dependencies;

public class DownloadReadTests
{
    /// <summary>A connection that went quiet: nothing comes, nothing ends.</summary>
    private sealed class Silent : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task Une_connexion_figee_finit_en_erreur_et_pas_en_attente_sans_fin()
    {
        // The PC slept or changed Wi-Fi during a download: past the headers,
        // HttpClient's timeout no longer applies, and the row spun forever.
        using var stream = new Silent();

        await Assert.ThrowsAsync<IOException>(() =>
            DownloadRead.NextAsync(stream, new byte[16], TimeSpan.FromMilliseconds(50), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Une_annulation_demandee_reste_une_annulation()
    {
        using var stream = new Silent();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DownloadRead.NextAsync(stream, new byte[16], TimeSpan.FromSeconds(30), cancel.Token).AsTask());
    }
}
