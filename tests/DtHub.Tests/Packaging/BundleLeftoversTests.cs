using DtHub.Infrastructure.Storage;

namespace DtHub.Tests.Packaging;

/// <summary>
/// Sorting of extraction folders. The actual cleanup touches %TEMP%
/// and is not exercised here: what matters is never to flag the
/// folder of the live version.
/// </summary>
public class BundleLeftoversTests
{
    private const string Root = @"C:\Users\x\AppData\Local\Temp\.net\DtHub";

    [Fact]
    public void Le_dossier_de_la_version_en_cours_est_epargne()
    {
        var stale = BundleLeftovers.Stale(
            [$@"{Root}\aaa", $@"{Root}\bbb", $@"{Root}\ccc"],
            $@"{Root}\bbb");

        Assert.Equal([$@"{Root}\aaa", $@"{Root}\ccc"], stale);
    }

    [Fact]
    public void La_casse_ne_fait_pas_effacer_le_dossier_en_cours()
    {
        // Windows is not case sensitive: comparing byte for byte would
        // delete the folder whose libraries are loaded.
        Assert.Empty(BundleLeftovers.Stale(
            [$@"{Root}\X8su9WP8OzRy"],
            $@"{Root}\x8su9wp8ozry"));
    }

    [Fact]
    public void Une_barre_finale_ne_change_rien()
    {
        Assert.Empty(BundleLeftovers.Stale([$@"{Root}\aaa\"], $@"{Root}\aaa"));
    }

    [Fact]
    public void Sans_rien_a_cote_il_n_y_a_rien_a_effacer()
    {
        Assert.Empty(BundleLeftovers.Stale([$@"{Root}\aaa"], $@"{Root}\aaa"));
    }
}
