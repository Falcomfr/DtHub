using DtHub.Core.Diagnostics;

namespace DtHub.Tests.Diagnostics;

/// <summary>
/// A report goes out to someone else. Whatever identifies a person or
/// their hardware must come out of it, and these tests use the forms
/// observed in real logs.
/// </summary>
public sealed class RedactionTests
{
    [Fact]
    public void Une_adresse_et_son_port_disparaissent()
    {
        var clean = Redaction.Apply("--> (tcpip) 192.168.1.16:38407 device 23078PND5G");

        Assert.DoesNotContain("192.168.1.16", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("38407", clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wireless debugging name carries the hardware serial number,
    /// which identifies the device in a stable way.
    /// </summary>
    [Fact]
    public void Le_nom_mdns_disparait_avec_le_serial_qu_il_porte()
    {
        var clean = Redaction.Apply(
            "(tcpip) adb-SERIAL0123456789-1V3FXQ._adb-tls-connect._tcp device");

        Assert.DoesNotContain("SERIAL0123456789", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("_adb-tls", clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// The original redaction works by exact match and does not bite
    /// into a value glued to its option. This is the limitation this
    /// class lifts.
    /// </summary>
    [Fact]
    public void Une_valeur_collee_a_son_option_disparait_aussi()
    {
        var clean = Redaction.Apply("scrcpy.exe --serial=192.168.1.16:38407 --max-fps=60");

        Assert.DoesNotContain("192.168.1.16", clean, StringComparison.Ordinal);
        Assert.Contains("--max-fps=60", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_nom_du_compte_windows_disparait_du_chemin()
    {
        var clean = Redaction.Apply(@"C:\Users\alice\AppData\Local\DtHub\tools\scrcpy.exe");

        Assert.DoesNotContain("alice", clean, StringComparison.Ordinal);
        Assert.Contains(@"AppData\Local\DtHub", clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whatever the application knows about goes away wherever it
    /// appears, matched on the longest string: a serial number caught
    /// inside a larger name must disappear along with it, not be cut in
    /// two.
    /// </summary>
    [Fact]
    public void Ce_que_l_application_connait_part_aussi()
    {
        var clean = Redaction.Apply(
            "Compte « Kerubim » ajouté sur Xiaomi 13T Pro (SERIAL0123456789)",
            ["SERIAL0123456789", "Kerubim"]);

        Assert.DoesNotContain("Kerubim", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("SERIAL0123456789", clean, StringComparison.Ordinal);
        Assert.Contains("ajouté sur Xiaomi 13T Pro", clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// A value that is too short is not redacted: an account named "A"
    /// would make every letter A in the report disappear.
    /// </summary>
    [Fact]
    public void Une_valeur_trop_courte_ne_biffe_rien()
    {
        var clean = Redaction.Apply("Le lancement a échoué.", ["A", "le"]);

        Assert.Equal("Le lancement a échoué.", clean);
    }

    [Fact]
    public void L_avertissement_de_chaleur_perd_l_appareil_et_garde_la_mesure()
    {
        // This line is logged as a warning, so it is always kept by the
        // digest, hence always present in a report pasted publicly. It
        // names the device: that is what must go, and nothing more,
        // since the measurement is the whole point of the report.
        const string ligne =
            "L'appareil 192.168.1.16:40335 se bride : état thermique 4, surface 35.107 °C.";

        var clean = Redaction.Apply(ligne);

        Assert.DoesNotContain("192.168.1.16", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("40335", clean, StringComparison.Ordinal);
        Assert.Contains("état thermique 4", clean, StringComparison.Ordinal);
        Assert.Contains("35.107", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void L_avertissement_de_chaleur_perd_aussi_l_appareil_nomme_par_mdns()
    {
        const string ligne =
            "L'appareil adb-SERIAL0123456789-1V3FXQ._adb-tls-connect._tcp se bride : "
            + "état thermique 3, surface 34.279 °C.";

        var clean = Redaction.Apply(ligne);

        Assert.DoesNotContain("SERIAL0123456789", clean, StringComparison.Ordinal);
        Assert.Contains("état thermique 3", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void Ce_qui_n_identifie_personne_reste_lisible()
    {
        const string message =
            "InvalidOperationException : Le thread appelant ne peut pas accéder à cet objet.";

        Assert.Equal(message, Redaction.Apply(message));
    }

    /// <summary>
    /// A screen's device name is a machine fingerprint with no
    /// diagnostic value. The geometry, however, says everything a
    /// placement needs, and it stays.
    /// </summary>
    [Fact]
    public void Le_nom_d_un_ecran_part_mais_sa_geometrie_reste()
    {
        var clean = Redaction.Apply(
            @"Écrans : \\.\DISPLAY11 3840x2160 en (0, 0) utile 3840x2088 en (0, 0)");

        Assert.DoesNotContain("DISPLAY11", clean, StringComparison.Ordinal);
        Assert.Contains("3840x2160", clean, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name of a launch profile is chosen by the person, and nothing
    /// stops them from putting their own nickname in it. No pattern
    /// recognizes it: it must be given to the redaction explicitly.
    /// </summary>
    [Fact]
    public void Un_nom_de_profil_choisi_part_s_il_est_donne()
    {
        const string ligne = "Session « Duo haute » retenue : 2 compte(s).";

        Assert.Contains("Duo haute", Redaction.Apply(ligne), StringComparison.Ordinal);
        Assert.DoesNotContain("Duo haute", Redaction.Apply(ligne, ["Duo haute"]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Un_texte_vide_ne_fait_rien_echouer(string? texte) =>
        Assert.Equal(string.Empty, Redaction.Apply(texte));
}
