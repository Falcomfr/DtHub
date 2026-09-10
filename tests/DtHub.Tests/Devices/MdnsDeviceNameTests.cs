using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class MdnsDeviceNameTests
{
    [Theory]
    [InlineData("adb-SERIAL0123456789-1V3FXQ._adb-tls-connect._tcp", "SERIAL0123456789")]
    [InlineData("adb-SERIAL0123456789-1V3FXQ._adb-tls-pairing._tcp", "SERIAL0123456789")]
    [InlineData("adb-R58M12ABCDE-a1B2c3._adb._tcp", "R58M12ABCDE")]
    public void Le_nom_mdns_porte_le_numero_de_serie(string name, string expected)
    {
        Assert.Equal(expected, MdnsDeviceName.HardwareSerialFrom(name));
    }

    [Fact]
    public void Un_numero_de_serie_a_tirets_garde_ses_tirets()
    {
        // C'est le dernier tiret qui sépare le jeton, pas le premier.
        Assert.Equal(
            "ABC-DEF-123",
            MdnsDeviceName.HardwareSerialFrom("adb-ABC-DEF-123-1V3FXQ._adb-tls-connect._tcp"));
    }

    [Theory]
    [InlineData("adb-SERIAL0123456789-1V3FXQ", "SERIAL0123456789")]
    [InlineData("adb-ABC-DEF-123-1V3FXQ", "ABC-DEF-123")]
    [InlineData("adb-R58M12ABCDE-a1B2c3._adb-tls-connect._tcp", "R58M12ABCDE")]
    public void Le_nom_d_instance_porte_le_numero_de_serie_sans_le_suffixe(string name, string expected)
    {
        // La colonne « nom » de `adb mdns services` ne porte pas le type de
        // service : il vit dans une colonne à part. Exiger le suffixe y
        // rejetait toute annonce.
        Assert.Equal(expected, MdnsDeviceName.HardwareSerialFromInstance(name));
    }

    [Fact]
    public void Le_suffixe_reste_exige_pour_un_numero_de_serie_rapporte_par_adb()
    {
        // C'est ce qui distingue un appareil injoignable, connu par son seul
        // nom d'annonce, d'un appareil branché qui rapporte son numéro.
        Assert.Null(MdnsDeviceName.HardwareSerialFrom("adb-SERIAL0123456789-1V3FXQ"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("192.168.1.16:38407")]
    [InlineData("23078PND5G")]
    [InlineData("adb-")]
    [InlineData("adb-._adb-tls-connect._tcp")]
    public void Ce_qui_n_est_pas_un_nom_mdns_est_refuse(string? serial)
    {
        Assert.Null(MdnsDeviceName.HardwareSerialFrom(serial));
        Assert.Null(MdnsDeviceName.HardwareSerialFromInstance(serial));
        Assert.False(MdnsDeviceName.IsMdnsName(serial));
    }
}
