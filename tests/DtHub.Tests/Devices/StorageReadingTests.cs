using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class StorageReadingTests
{
    /// <summary>
    /// Relevé au caractère près sur le Xiaomi 13T Pro, Android 16,
    /// <c>adb shell df /data</c>.
    /// </summary>
    private const string Releve = """
        Filesystem       1K-blocks      Used Available Use% Mounted on
        /dev/block/dm-59 485636064 171563720 313535476  36% /data/user/0
        """;

    [Fact]
    public void La_place_libre_se_lit()
    {
        var storage = StorageReading.Parse(Releve);

        Assert.NotNull(storage);
        Assert.Equal(313535476L * 1024, storage!.FreeBytes);
        Assert.Equal(299.0, storage.FreeGigabytes, precision: 0);
    }

    [Fact]
    public void Un_volume_dont_le_nom_porte_un_espace_ne_decale_rien()
    {
        // La colonne se compte depuis la droite pour cette raison : compter
        // depuis la gauche décalerait tout dès qu'un nom contient un espace.
        const string releve = """
            Filesystem       1K-blocks      Used Available Use% Mounted on
            /dev/block/mon volume 485636064 171563720 313535476  36% /data/user/0
            """;

        Assert.Equal(313535476L * 1024, StorageReading.Parse(releve)!.FreeBytes);
    }

    [Fact]
    public void Un_appareil_bien_rempli_le_dit()
    {
        var plein = Releve.Replace("313535476", "1048576", StringComparison.Ordinal);

        var storage = StorageReading.Parse(plein);

        Assert.True(storage!.IsLow);
        Assert.NotNull(storage.Describe());
    }

    [Fact]
    public void Un_appareil_au_bord_du_vide_durcit_le_message()
    {
        var bas = Releve.Replace("313535476", "1048576", StringComparison.Ordinal);
        var critique = Releve.Replace("313535476", "102400", StringComparison.Ordinal);

        Assert.NotEqual(
            StorageReading.Parse(bas)!.Describe(),
            StorageReading.Parse(critique)!.Describe());
    }

    [Fact]
    public void Trois_cents_gigaoctets_libres_ne_disent_rien()
    {
        var storage = StorageReading.Parse(Releve);

        Assert.False(storage!.IsLow);
        Assert.Null(storage.Describe());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("df: /data: Permission denied")]
    [InlineData("Filesystem 1K-blocks Used Available Use% Mounted on")]
    public void Une_sortie_inexploitable_ne_rend_rien(string? df)
    {
        Assert.Null(StorageReading.Parse(df));
    }

    [Fact]
    public void Un_en_tete_sans_colonne_libre_ne_rend_rien()
    {
        const string releve = """
            Filesystem Size Used Use% Mounted on
            /dev/block/dm-59 463G 164G 36% /data/user/0
            """;

        Assert.Null(StorageReading.Parse(releve));
    }
}
