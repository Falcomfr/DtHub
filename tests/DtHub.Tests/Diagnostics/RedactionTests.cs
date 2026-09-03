using DtHub.Core.Diagnostics;

namespace DtHub.Tests.Diagnostics;

/// <summary>
/// Un rapport part chez quelqu'un d'autre. Ce qui désigne une personne ou son
/// matériel doit en sortir, et ces épreuves emploient les formes relevées dans
/// de vrais journaux.
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
    /// Le nom du débogage sans fil porte le numéro de série matériel, qui
    /// identifie l'appareil de façon stable.
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
    /// La biffure d'origine se fait par égalité exacte et ne mord pas sur une
    /// valeur collée à son option. C'est la réserve que cette classe lève.
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
    /// Ce que l'application connaît part où qu'il soit, et par la chaîne la plus
    /// longue : un numéro de série pris dans un nom plus grand doit disparaître
    /// avec lui, non le couper en deux.
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
    /// Une valeur trop courte n'est pas biffée : un compte nommé « A » ferait
    /// disparaître toutes les lettres A du rapport.
    /// </summary>
    [Fact]
    public void Une_valeur_trop_courte_ne_biffe_rien()
    {
        var clean = Redaction.Apply("Le lancement a échoué.", ["A", "le"]);

        Assert.Equal("Le lancement a échoué.", clean);
    }

    [Fact]
    public void Ce_qui_n_identifie_personne_reste_lisible()
    {
        const string message =
            "InvalidOperationException : Le thread appelant ne peut pas accéder à cet objet.";

        Assert.Equal(message, Redaction.Apply(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Un_texte_vide_ne_fait_rien_echouer(string? texte) =>
        Assert.Equal(string.Empty, Redaction.Apply(texte));
}
