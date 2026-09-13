using DtHub.Core.Diagnostics;

namespace DtHub.Tests.Diagnostics;

/// <summary>
/// The report goes out to someone else: what it carries is a
/// commitment.
/// </summary>
public sealed class DiagnosticReportTests
{
    private static readonly DiagnosticFacts Faits = new(
        "DT Hub 0.1.0",
        "Windows 11 26100",
        ".NET 10.0.11",
        "fr / fr-FR",
        "Écrans : 3840x2160 + 1920x1080",
        "Appareils : 1",
        "Comptes ouverts : 2, dont 0 en onglets");

    [Fact]
    public void Le_rapport_porte_de_quoi_comprendre()
    {
        var report = DiagnosticReport.Compose(
            "Une erreur inattendue.",
            Faits,
            new InvalidOperationException("Le thread appelant ne peut pas accéder."));

        Assert.Contains("Une erreur inattendue.", report, StringComparison.Ordinal);
        Assert.Contains("DT Hub 0.1.0", report, StringComparison.Ordinal);
        Assert.Contains("Windows 11 26100", report, StringComparison.Ordinal);
        Assert.Contains("3840x2160", report, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", report, StringComparison.Ordinal);
        Assert.Contains("Le thread appelant", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// The point that matters: nothing that identifies a person or
    /// their hardware must get out, no matter where in the report
    /// it comes from.
    /// </summary>
    [Fact]
    public void Rien_de_personnel_n_en_ressort()
    {
        var report = DiagnosticReport.Compose(
            "Ouverture refusée.",
            Faits,
            lastFailure: @"scrcpy.exe --serial=192.168.1.16:38407 sous C:\Users\alice\AppData\Local",
            log: "adb-SERIAL0123456789-1V3FXQ._adb-tls-connect._tcp device 23078PND5G",
            secrets: ["SERIAL0123456789", "Kerubim"]);

        Assert.DoesNotContain("192.168.1.16", report, StringComparison.Ordinal);
        Assert.DoesNotContain("38407", report, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", report, StringComparison.Ordinal);
        Assert.DoesNotContain("SERIAL0123456789", report, StringComparison.Ordinal);
        Assert.DoesNotContain("_adb-tls", report, StringComparison.Ordinal);

        // What remains must stay useful.
        Assert.Contains("Ouverture refusée.", report, StringComparison.Ordinal);
        Assert.Contains("scrcpy.exe", report, StringComparison.Ordinal);
    }

    [Fact]
    public void La_cause_d_une_exception_est_dite()
    {
        var report = DiagnosticReport.Compose(
            "Échec.",
            Faits,
            new InvalidOperationException("Dessus", new TimeoutException("Dessous")));

        Assert.Contains("TimeoutException", report, StringComparison.Ordinal);
        Assert.Contains("Dessous", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Sans_faute_le_rapport_vaut_quand_meme()
    {
        var report = DiagnosticReport.Compose("Signaler un problème", Faits);

        Assert.Contains("DT Hub 0.1.0", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// A report's URL carries the title, never the body: a report
    /// goes far beyond what a URL can accept.
    /// </summary>
    [Fact]
    public void L_adresse_de_signalement_porte_le_titre_et_pas_le_rapport()
    {
        var url = DiagnosticReport.IssueUrl("https://github.com/Falcomfr/DtHub", "Une erreur inattendue.");

        Assert.StartsWith("https://github.com/Falcomfr/DtHub/issues/new?template=bug.yml", url, StringComparison.Ordinal);
        Assert.Contains("title=", url, StringComparison.Ordinal);
        Assert.True(url.Length < 400, $"l'adresse fait {url.Length} caractères");
    }

    [Fact]
    public void Un_titre_trop_long_est_coupe()
    {
        var url = DiagnosticReport.IssueUrl("https://github.com/x/y", new string('a', 400));

        Assert.True(url.Length < 400, $"l'adresse fait {url.Length} caractères");
    }
}
