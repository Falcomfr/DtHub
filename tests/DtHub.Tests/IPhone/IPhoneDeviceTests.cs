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

    [Theory]
    [InlineData("iPad", false)]
    [InlineData("iPhone", true)]
    [InlineData("", true)]
    public void Un_iPad_s_ouvre_sans_le_son(string deviceClass, bool sound)
    {
        // An iPad Pro M4 on iPadOS 27.2.1 showed its screen but took no click
        // (2026-11-11); its last difference from the iPhone that took clicks
        // was the sound stream, added in 0.14.5.
        var device = new IPhoneDevice("UDID", "Appareil", "27.2.1", "USB", deviceClass);

        Assert.Equal(sound, device.StreamsSound);
    }

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

    [Theory]
    // An iPad player looks for iPadOS in the Settings, not iOS.
    [InlineData("iPhone", "iOS")]
    [InlineData("iPad", "iPadOS")]
    [InlineData("", "iOS/iPadOS")]
    public void Le_systeme_nomme_suit_le_type_d_appareil(string deviceClass, string system)
    {
        var device = new IPhoneDevice("udid", "x", "26.4", "USB", deviceClass);

        Assert.Equal(system, device.SystemName);
    }

    [Fact]
    public void La_liste_donne_le_type_d_appareil()
    {
        var device = Assert.Single(IPhoneDevice.ParseList(XrSample));

        Assert.Equal("iOS", device.SystemName);
    }

    [Fact]
    public void Un_champ_absent_ne_fait_pas_echouer_la_lecture()
    {
        var device = Assert.Single(IPhoneDevice.ParseList("""[{ "Identifier": "abc", "DeviceName": null }]"""));

        Assert.Equal("abc", device.Udid);
        Assert.Equal(string.Empty, device.IosVersion);
        Assert.Equal("iOS/iPadOS", device.SystemName);
        Assert.False(device.CanMirror);
    }

    [Fact]
    public void Aucune_liste_aucun_appareil()
    {
        Assert.Empty(IPhoneDevice.ParseList("[]"));
    }

    [Theory]
    [InlineData("Failed to connect")]
    [InlineData("{}")]
    [InlineData("null")]
    public void Une_sortie_qui_n_est_pas_une_liste_echoue_en_json(string output)
    {
        // The only exception the caller turns into a message for the player.
        Assert.ThrowsAny<JsonException>(() => IPhoneDevice.ParseList(output));
    }

    [Fact]
    public void Un_element_sans_identifiant_est_ecarte()
    {
        Assert.Empty(IPhoneDevice.ParseList("""[42, { "DeviceName": "x" }]"""));
    }
}
