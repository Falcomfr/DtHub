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
}
