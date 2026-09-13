using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class DungeonPageParserTests
{
    /// <summary>
    /// Captured on "Atelier du Tanukouï San". The thumbnails from
    /// Ankama's servers are stripped out of the fragment: none of
    /// them serve any purpose here, and the repository hosts none of
    /// them.
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

    /// <summary>
    /// Captured on "Château de Belladone", which does not require a
    /// key.
    /// </summary>
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
        // The site writes "Clef : " for the benefit of screen
        // readers. Once the markup is stripped, nothing would
        // distinguish it from the key's name anymore.
        Assert.Equal("Clef de l'Atelier du Tanukouï San", DungeonPageParser.ParseKey(AvecClef));
    }

    [Fact]
    public void Un_donjon_sans_clef_n_en_rend_aucune()
    {
        // Nine dungeons out of eighty-two are in this case: the
        // absence is information, not a gap to fill.
        Assert.Equal(string.Empty, DungeonPageParser.ParseKey(SansClef));
        Assert.Equal(string.Empty, DungeonPageParser.ParseKey(null));
    }

    [Fact]
    public void Une_page_sans_bloc_ne_rend_rien()
    {
        // One dungeon out of eighty-three does not have the
        // structured block.
        Assert.Equal(string.Empty, DungeonPageParser.ParseSoulStone("<p>Rien ici.</p>"));
        Assert.Empty(DungeonPageParser.ParseSections("<p>Rien ici.</p>"));
    }

    [Fact]
    public void Les_titres_de_sections_sortent_dans_l_ordre_de_la_page()
    {
        // Captured on "Château de Belladone", markup included.
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
        // Two guides use the same subheading twice; two steps with
        // the same name would not say where one is.
        Assert.Equal(
            ["Boss"],
            DungeonPageParser.ParseSections("<h2>Boss</h2><h2>Boss</h2>"));
    }
}
