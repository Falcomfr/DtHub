using System.Text.Json;

using DtHub.Core.IPhone;

namespace DtHub.Tests.IPhone;

/// <summary>
/// The list printed by <c>pymobiledevice3 usbmux list</c>. The sample is the
/// one read on 2026-11-04 from an iPhone XR, name included: Apple left a
/// space in front of it.
/// </summary>
public class IPhoneDeviceTests
{
    private const string XrSample = """
        [
            {
                "BuildVersion": "22H355",
                "ConnectionType": "USB",
                "DeviceClass": "iPhone",
                "DeviceName": " lara.croft - iPhone (2)",
                "Identifier": "00008020-000A5909262A002E",
                "ProductType": "iPhone11,8",
                "ProductVersion": "18.7.9",
                "UniqueDeviceID": "00008020-000A5909262A002E"
            }
        ]
        """;

    [Fact]
    public void La_liste_donne_l_identifiant_le_nom_et_la_version()
    {
        var device = Assert.Single(IPhoneDevice.ParseList(XrSample));

        Assert.Equal("00008020-000A5909262A002E", device.Udid);
        Assert.Equal("lara.croft - iPhone (2)", device.Name);
        Assert.Equal("18.7.9", device.IosVersion);
        Assert.Equal("USB", device.ConnectionType);
    }

    [Fact]
    public void Un_iPhone_en_iOS_18_ne_peut_pas_etre_affiche()
    {
        var device = Assert.Single(IPhoneDevice.ParseList(XrSample));

        Assert.False(device.CanMirror);
    }

    [Theory]
    [InlineData("27.0", true)]
    [InlineData("27.1.2", true)]
    [InlineData("28.0", true)]
    [InlineData("26.4", false)]
    [InlineData("", false)]
    public void L_affichage_demande_iOS_27(string version, bool expected)
    {
        var device = new IPhoneDevice("udid", "iPhone", version, "USB");

        Assert.Equal(expected, device.CanMirror);
    }

    [Fact]
    public void Un_champ_absent_ne_fait_pas_echouer_la_lecture()
    {
        var device = Assert.Single(IPhoneDevice.ParseList("""[{ "Identifier": "abc", "DeviceName": null }]"""));

        Assert.Equal("abc", device.Udid);
        Assert.Equal(string.Empty, device.IosVersion);
        Assert.False(device.CanMirror);
    }

    [Fact]
    public void Aucune_liste_aucun_appareil()
    {
        Assert.Empty(IPhoneDevice.ParseList("[]"));
    }

    [Fact]
    public void Une_sortie_illisible_echoue()
    {
        Assert.ThrowsAny<JsonException>(() => IPhoneDevice.ParseList("Failed to connect"));
    }
}
