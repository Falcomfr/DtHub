using DtHub.Core.Adb;
using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class AdbTransportChoiceTests
{
    // The lines are the ones a real phone returned when attached
    // twice, except that the serial number has been replaced.
    private const string Adresse =
        "192.168.1.14:40187 device product:corot_global model:23078PND5G device:corot transport_id:5";

    private const string Nom =
        "adb-MATERIEL123-1V3FXQ._adb-tls-connect._tcp device product:corot_global "
        + "model:23078PND5G device:corot transport_id:6";

    private static AdbDeviceEntry Ligne(string texte) => AdbOutputParser.ParseDeviceLine(texte)!;

    private static AndroidDevice Memorise(
        string id = "MATERIEL123",
        string serial = "192.168.1.14:40187",
        string? adresse = "192.168.1.14",
        int? port = 40187) =>
        new()
        {
            Id = id,
            Serial = serial,
            State = AdbDeviceState.Device,
            ConnectionKind = AdbConnectionKind.Wireless,
            LastKnownAddress = adresse,
            LastKnownPort = port,
            IsPaired = true,
        };

    [Fact]
    public void Le_nom_mdns_cede_devant_l_adresse_du_meme_telephone()
    {
        var gardes = AdbTransportChoice.WithoutDoubles(
            [Ligne(Adresse), Ligne(Nom)],
            [Memorise()]);

        Assert.Equal(["192.168.1.14:40187"], gardes.Select(e => e.Serial));
    }

    /// <summary>
    /// ADB's order must not decide anything: the name yields wherever
    /// it is.
    /// </summary>
    [Fact]
    public void Peu_importe_l_ordre_ou_ADB_les_rend()
    {
        var gardes = AdbTransportChoice.WithoutDoubles(
            [Ligne(Nom), Ligne(Adresse)],
            [Memorise()]);

        Assert.Equal(["192.168.1.14:40187"], gardes.Select(e => e.Serial));
    }

    /// <summary>
    /// Alone, the mDNS name is the only address we have: discarding it
    /// would make the phone disappear from the list.
    /// </summary>
    [Fact]
    public void Un_nom_mdns_seul_est_conserve()
    {
        var gardes = AdbTransportChoice.WithoutDoubles([Ligne(Nom)], [Memorise()]);

        Assert.Single(gardes);
        Assert.StartsWith("adb-MATERIEL123", gardes[0].Serial, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name of one phone, the address of another: two devices,
    /// two lines. Discarding either would mean losing a phone.
    /// </summary>
    [Fact]
    public void Deux_telephones_distincts_gardent_leurs_deux_lignes()
    {
        var gardes = AdbTransportChoice.WithoutDoubles(
            [Ligne(Adresse), Ligne("adb-AUTRE456-9Z9Z9Z._adb-tls-connect._tcp device transport_id:7")],
            [Memorise(), Memorise(id: "AUTRE456", serial: "10.0.0.9:5555", adresse: "10.0.0.9", port: 5555)]);

        Assert.Equal(2, gardes.Count);
    }

    /// <summary>
    /// A device the registry does not know is not matched to its
    /// mDNS name: we do not yet know it is the same one. An extra
    /// line is better than mistaking one phone for another.
    /// </summary>
    [Fact]
    public void Un_inconnu_garde_ses_deux_lignes()
    {
        var gardes = AdbTransportChoice.WithoutDoubles([Ligne(Adresse), Ligne(Nom)], []);

        Assert.Equal(2, gardes.Count);
    }

    [Fact]
    public void Une_liste_sans_nom_mdns_est_rendue_telle_quelle()
    {
        var entrees = new[] { Ligne(Adresse), Ligne("USB0001 device usb:1-2 transport_id:1") };

        var gardes = AdbTransportChoice.WithoutDoubles(entrees, [Memorise()]);

        Assert.Equal(entrees.Select(e => e.Serial), gardes.Select(e => e.Serial));
    }

    /// <summary>
    /// The cable and the mDNS name of the same phone: the cable is
    /// the one that speaks, and the registry recognizes it by its
    /// hardware serial number.
    /// </summary>
    [Fact]
    public void Le_nom_mdns_cede_aussi_devant_le_cable()
    {
        var gardes = AdbTransportChoice.WithoutDoubles(
            [Ligne("MATERIEL123 device usb:1-2 transport_id:1"), Ligne(Nom)],
            [Memorise(serial: "MATERIEL123", adresse: null, port: null)]);

        Assert.Equal(["MATERIEL123"], gardes.Select(e => e.Serial));
    }

    [Fact]
    public void Une_liste_vide_ne_leve_pas() =>
        Assert.Empty(AdbTransportChoice.WithoutDoubles([], [Memorise()]));
}
