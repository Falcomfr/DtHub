using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class DungeonPageParserTests
{
    /// <summary>
    /// Relevé sur « Atelier du Tanukouï San ». Les vignettes venues des
    /// serveurs d'Ankama sont retirées du fragment : rien de leur ne sert ici,
    /// et le dépôt n'en héberge aucune.
    /// </summary>
    private const string AvecClef =
        """
        <span class="pcd-info__soul-stone">gigantesque pierre d’âme</span>
        <div class="pcd-info__row pcd-info__row--key">
          <span class="pcd-icon pcd-icon--key-image"></span>
          <strong class="pcd-info__value">
            <span class="screen-reader-text">Clef : </span>
            <a href="https://www.dofus-touch.com/fr/mmorpg/encyclopedie/ressources/17632">
              Clef de l'Atelier du Tanukouï San <span class="pcd-icon pcd-icon--external"></span>
            </a>
          </strong>
        </div>
        """;

    /// <summary>Relevé sur « Château de Belladone », qui n'exige pas de clef.</summary>
    private const string SansClef =
        """<span class="pcd-info__soul-stone">petite pierre d’âme</span>""";

    [Fact]
    public void La_pierre_d_ame_se_lit_avec_sa_taille()
    {
        Assert.Equal("gigantesque pierre d’âme", DungeonPageParser.ParseSoulStone(AvecClef));
        Assert.Equal("petite pierre d’âme", DungeonPageParser.ParseSoulStone(SansClef));
    }

    [Fact]
    public void La_clef_perd_le_libelle_de_lecture_d_ecran()
    {
        // Le site écrit « Clef : » à l'usage des lecteurs d'écran. Une fois le
        // balisage tombé, plus rien ne le distinguerait du nom de la clef.
        Assert.Equal("Clef de l'Atelier du Tanukouï San", DungeonPageParser.ParseKey(AvecClef));
    }

    [Fact]
    public void Un_donjon_sans_clef_n_en_rend_aucune()
    {
        // Neuf donjons sur quatre-vingt-deux sont dans ce cas : l'absence est
        // une information, pas un trou à combler.
        Assert.Equal(string.Empty, DungeonPageParser.ParseKey(SansClef));
        Assert.Equal(string.Empty, DungeonPageParser.ParseKey(null));
    }

    [Fact]
    public void Une_page_sans_bloc_ne_rend_rien()
    {
        // Un donjon sur quatre-vingt-trois n'a pas le bloc structuré.
        Assert.Equal(string.Empty, DungeonPageParser.ParseSoulStone("<p>Rien ici.</p>"));
        Assert.Empty(DungeonPageParser.ParseSections("<p>Rien ici.</p>"));
    }

    [Fact]
    public void Les_titres_de_sections_sortent_dans_l_ordre_de_la_page()
    {
        // Relevé sur « Château de Belladone », balisage compris.
        const string page =
            """
            <h2>Position du PNJ sur la carte</h2>
            <h2 class="wp-block-heading">Monstres</h2>
            <h2 class="wp-block-heading">Liste des salles</h2>
            <h2 class="wp-block-heading">Boss</h2>
            <h2 class="wp-block-heading">Mécanique du donjon</h2>
            <h2 class="wp-block-heading has-text-align-left">Les Succès</h2>
            <h2 class="wp-block-heading">Fin du donjon</h2>
            <h2 id="papycha-thanks-title-29503">Papycha remercie</h2>
            """;

        Assert.Equal(
            ["Monstres", "Liste des salles", "Boss", "Mécanique du donjon", "Les Succès", "Fin du donjon"],
            DungeonPageParser.ParseSections(page));
    }

    [Fact]
    public void Un_titre_repete_ne_compte_qu_une_fois()
    {
        // Deux guides emploient deux fois le même intertitre ; deux étapes de
        // même nom ne diraient pas où l'on est.
        Assert.Equal(
            ["Boss"],
            DungeonPageParser.ParseSections("<h2>Boss</h2><h2>Boss</h2>"));
    }
}
