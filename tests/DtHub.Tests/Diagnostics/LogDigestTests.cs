using DtHub.Core.Diagnostics;

namespace DtHub.Tests.Diagnostics;

/// <summary>
/// A day's log runs to 1700 lines, of which 7 percent are errors.
/// These tests pin down what we keep from it.
/// </summary>
public sealed class LogDigestTests
{
    private const string Journal = """
        2026-09-03 10:00:00.000 [INF] [a1b2c3] DT Hub 0.1.0 démarre.
        2026-09-03 10:00:01.000 [INF] [a1b2c3] Écrans : 3840x2160.
        2026-09-03 10:00:02.000 [WRN] [a1b2c3] Le téléphone n'a pas répondu.
        2026-09-03 10:00:03.000 [INF] [a1b2c3] Lancement terminé.
        2026-09-03 10:00:04.000 [INF] [a1b2c3] Raccourcis actifs.
        """;

    [Fact]
    public void Un_avertissement_est_toujours_gardé()
    {
        var digest = LogDigest.Of(Journal, session: null, tail: 1);

        Assert.Contains("Le téléphone n'a pas répondu.", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void Les_dernieres_lignes_sont_gardees()
    {
        var digest = LogDigest.Of(Journal, session: null, tail: 2);

        Assert.Contains("Raccourcis actifs.", digest, StringComparison.Ordinal);
        Assert.Contains("Lancement terminé.", digest, StringComparison.Ordinal);
    }

    /// <summary>
    /// The quiet middle stretch is dropped, and a gap marker says a
    /// skip happened: without it, two errors an hour apart would read
    /// as two errors in a row.
    /// </summary>
    [Fact]
    public void Ce_qui_est_saute_se_voit()
    {
        var digest = LogDigest.Of(Journal, session: null, tail: 1);

        Assert.DoesNotContain("Écrans", digest, StringComparison.Ordinal);
        Assert.Contains("[…]", digest, StringComparison.Ordinal);
    }

    /// <summary>
    /// 408 startups over 6 days get mixed together in 7 files: without
    /// the session, a report would drag in the previous day's errors.
    /// </summary>
    [Fact]
    public void Seule_la_session_en_cours_est_prise()
    {
        const string deux = """
            2026-09-03 09:00:00.000 [INF] [vieux1] DT Hub 0.1.0 démarre.
            2026-09-03 09:00:01.000 [ERR] [vieux1] Faute de la session d'avant.
            2026-09-03 10:00:00.000 [INF] [neuve2] DT Hub 0.1.0 démarre.
            2026-09-03 10:00:01.000 [ERR] [neuve2] Faute de la session en cours.
            """;

        var digest = LogDigest.Of(deux, "neuve2", tail: 1);

        Assert.Contains("session en cours", digest, StringComparison.Ordinal);
        Assert.DoesNotContain("session d'avant", digest, StringComparison.Ordinal);
    }

    /// <summary>
    /// A call stack carries no timestamp: it must stay attached to the
    /// message that produced it, otherwise the report would render an
    /// error without its stack, or a stack without its error.
    /// </summary>
    [Fact]
    public void Une_pile_d_appel_reste_avec_son_message()
    {
        const string avecPile = """
            2026-09-03 10:00:00.000 [INF] [a1b2c3] DT Hub démarre.
            2026-09-03 10:00:02.000 [FTL] [a1b2c3] Une erreur inattendue.
            InvalidOperationException : Le thread appelant ne peut pas accéder.
               à DtHub.App.Services.GameLauncher.OnForegroundChanged()
            2026-09-03 10:00:03.000 [INF] [a1b2c3] Suite.
            """;

        var digest = LogDigest.Of(avecPile, session: null, tail: 1);

        Assert.Contains("Une erreur inattendue.", digest, StringComparison.Ordinal);
        Assert.Contains("OnForegroundChanged", digest, StringComparison.Ordinal);
    }

    /// <summary>A report gets pasted into a form: it has a limit.</summary>
    [Fact]
    public void Le_rapport_ne_deborde_pas()
    {
        var gros = string.Join(
            '\n',
            Enumerable.Range(0, 4000).Select(i =>
                $"2026-09-03 10:00:00.000 [WRN] [a1b2c3] Avertissement numéro {i}."));

        var digest = LogDigest.Of(gros, session: null);

        Assert.True(
            digest.Length <= LogDigest.MaxLength + 200,
            $"le condensé fait {digest.Length} caractères");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_journal_vide_ne_fait_rien_echouer(string? journal) =>
        Assert.Equal(string.Empty, LogDigest.Of(journal, session: null));
}
