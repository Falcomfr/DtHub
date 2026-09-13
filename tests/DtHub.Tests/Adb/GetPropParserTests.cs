using DtHub.Core.Adb;

namespace DtHub.Tests.Adb;

public class GetPropParserTests
{
    private const string Output = """
        [ro.product.manufacturer]: [Xiaomi]
        [ro.product.model]: [23078RKD5G]
        [ro.product.device]: [aristotle]
        [ro.build.version.release]: [14]
        [ro.build.version.sdk]: [34]
        [ro.build.description]: [aristotle-user 14 UKQ1.230804.001 [release-keys]]
        [persist.vide]: []
        """;

    [Fact]
    public void Sortie_vide_ou_absente_ne_leve_pas()
    {
        Assert.Empty(AdbOutputParser.ParseGetProp(null));
        Assert.Empty(AdbOutputParser.ParseGetProp(string.Empty));
        Assert.Empty(AdbOutputParser.ParseGetProp("bruit sans crochets\n"));
    }

    [Fact]
    public void Les_proprietes_attendues_sont_lues()
    {
        var properties = AdbOutputParser.ParseGetProp(Output);

        Assert.Equal("Xiaomi", properties["ro.product.manufacturer"]);
        Assert.Equal("23078RKD5G", properties["ro.product.model"]);
        Assert.Equal("14", properties["ro.build.version.release"]);
        Assert.Equal("34", properties["ro.build.version.sdk"]);
    }

    [Fact]
    public void Une_valeur_contenant_des_crochets_reste_entiere()
    {
        var properties = AdbOutputParser.ParseGetProp(Output);

        Assert.Equal(
            "aristotle-user 14 UKQ1.230804.001 [release-keys]",
            properties["ro.build.description"]);
    }

    [Fact]
    public void Une_valeur_vide_est_conservee()
    {
        var properties = AdbOutputParser.ParseGetProp(Output);

        Assert.True(properties.ContainsKey("persist.vide"));
        Assert.Equal(string.Empty, properties["persist.vide"]);
    }

    [Fact]
    public void Les_fins_de_ligne_windows_sont_supportees()
    {
        var properties = AdbOutputParser.ParseGetProp("[a]: [1]\r\n[b]: [2]\r\n");

        Assert.Equal("1", properties["a"]);
        Assert.Equal("2", properties["b"]);
    }
}
