using System.Globalization;

using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class ItemSourceTextTests : IDisposable
{
    public ItemSourceTextTests() => Strings.Speak(CultureInfo.GetCultureInfo("fr"));

    public void Dispose()
    {
        Strings.Speak(null);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Chaque_source_sur_sa_ligne_sous_le_nom_de_l_objet()
    {
        var source = new ItemSource(
            [
                new MonsterSource(1, "Bouftou", 55, false, null, ["Plaine", "Bois"]),
                new MonsterSource(2, "Bwork", 30, false, null, [], Conditional: true),
                new MonsterSource(3, "Susbewl", 60, true, "Bwork mage", ["Village"]),
                new MonsterSource(4, "Archi seul", 40, true, null, []),
            ],
            [new JobLevel("Bûcheron", 100)],
            [new JobLevel("Alchimiste", 20)],
            false,
            "Bois de Tremble");

        Assert.Equal(
            """
            Bois de Tremble :
            Lâché par Bouftou (niv. 55) · Plaine, Bois
            Lâché par Bwork (niv. 30), pendant la quête
            Archimonstre : Susbewl (niv. 60) (version normale : Bwork mage) · Village
            Archimonstre : Archi seul (niv. 40)
            Récolte : Bûcheron niv. 100
            Fabrication : Alchimiste niv. 20
            """.ReplaceLineEndings("\n"),
            ItemSourceText.Of(source));
    }

    [Fact]
    public void Sans_source_connue_on_le_dit_et_un_nom_absent_ne_laisse_pas_de_ligne() =>
        Assert.Equal("Source inconnue", ItemSourceText.Of(new ItemSource([], [], [], true)));

    private static MonsterSource Monstre(string nom, int niveau, params string[] zones) =>
        new(0, nom, niveau, false, null, zones);

    [Fact]
    public void Les_monstres_des_memes_zones_partagent_une_ligne()
    {
        var source = new ItemSource(
            [
                Monstre("Crapeur", 161, "Larmes", "Hypogée"),
                Monstre("Atomystique", 160, "Larmes", "Hypogée"),
                Monstre("Mofette", 164, "Autre"),
                Monstre("Crapeur", 161, "Larmes", "Hypogée"),
            ],
            [],
            [],
            false,
            "Oeuf vapeur");

        Assert.Equal(
            "Oeuf vapeur :\nLâché par Crapeur (niv. 161), Atomystique (niv. 160) · Larmes, Hypogée\nLâché par Mofette (niv. 164) · Autre",
            ItemSourceText.Of(source));
    }

    [Fact]
    public void L_en_tete_garde_trois_sources_et_laisse_le_reste_au_survol()
    {
        ItemSource Objet(string nom, params MonsterSource[] monstres) => new(monstres, [], [], monstres.Length == 0, nom);
        var potion = Objet("Potion");
        var pierre = Objet("Pierre", Monstre("Atomystique", 160, "Larmes"));
        var coeur = Objet("Coeur", Monstre("Crapeur", 161, "Larmes"));
        var oeuf = Objet("Oeuf", Monstre("Mofette", 164, "Autre"));

        Assert.Equal(
            "Potion : Source inconnue · Pierre : Lâché par Atomystique (niv. 160) · Larmes · Coeur : Lâché par Crapeur (niv. 161) · Larmes · …",
            ItemSourceText.Line([potion, pierre, coeur, oeuf]));
        Assert.Equal(
            "Potion : Source inconnue · Pierre : Lâché par Atomystique (niv. 160) · Larmes",
            ItemSourceText.Line([potion, pierre]));
        Assert.Equal("Lâché par Mofette (niv. 164) · Autre", ItemSourceText.Line([oeuf with { Name = null }]));
    }
}
