using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestFollowUpsTests
{
    /// <summary>
    /// Relevé au caractère près sur la page « Le Dragon d'Astrub », colonne
    /// « Quêtes et jalons suivants ». Trois liens de trois natures : un succès
    /// validé posé hors de tout groupe, puis deux quêtes rangées chacune sous
    /// son objectif. C'est la forme que le bouton unique ne savait pas montrer.
    /// </summary>
    private const string PiedDArticle = """
        <nav class="pqt-progress" aria-label="Progression de la quête">
        <section class="pqt-progress__column pqt-progress__column--next">
        <h3>Quêtes et jalons suivants</h3>
        <div class="pqt-progress__items"><a class="pqt-progress__item pqt-progress__item--success" href="https://papycha.fr/succes/?pqt_success=success:cards.devenir-une-legende#succes-selectionne"><small>Succès validé</small><strong>[Succès] Devenir une légende</strong></a><section class="pqt-progress__objective-group"><header class="pqt-progress__objective-title"><span>Objectif :</span><strong>Dofus Cawotte obtenu</strong></header><div class="pqt-progress__objective-quests"><a class="pqt-progress__objective-quest pqt-progress__objective-quest--quest" href="https://papycha.fr/quete-votre-premier-dofus/"><strong>Un nouveau Dofus ?</strong></a></div></section><section class="pqt-progress__objective-group"><header class="pqt-progress__objective-title"><span>Objectif :</span><strong>Suite du parcours</strong></header><div class="pqt-progress__objective-quests"><a class="pqt-progress__objective-quest pqt-progress__objective-quest--quest" href="https://papycha.fr/quete-la-decouverte-dun-vaste-monde/"><strong>La découverte d’un vaste monde !</strong></a></div></section></div>
        </section>
        </nav>
        """;

    private static IReadOnlyList<QuestFollowUpGroup> Lues(string? url = null) =>
        QuestFollowUps.Of(QuestPageParser.ParseChain(PiedDArticle), url);

    [Fact]
    public void Les_trois_suites_du_pied_d_article_sont_rendues()
    {
        // Le bouton unique n'en montrait aucune : la colonne en nomme deux, et
        // « en désigner une mentirait ». La liste, elle, peut tout montrer.
        Assert.Equal(3, QuestFollowUps.Count(Lues()));
    }

    [Fact]
    public void Les_objectifs_du_site_sont_conserves()
    {
        var groupes = Lues();

        Assert.Equal(
            [string.Empty, "Dofus Cawotte obtenu", "Suite du parcours"],
            groupes.Select(g => g.Objective));
    }

    [Fact]
    public void L_ordre_du_site_est_conserve()
    {
        // Regrouper par nom d'objectif réordonnerait la colonne, et cet ordre
        // est précisément ce que le site publie.
        Assert.Equal(
            ["[Succès] Devenir une légende", "Un nouveau Dofus ?", "La découverte d’un vaste monde !"],
            Lues().SelectMany(g => g.Links).Select(l => l.Title));
    }

    [Fact]
    public void Les_trois_natures_restent_distinguables()
    {
        // L'utilisateur a demandé à voir les trois, chacune marquée : c'est ce
        // que le site publie, et trier à sa place déciderait pour lui.
        Assert.Equal(
            [QuestLinkKind.Success, QuestLinkKind.Quest, QuestLinkKind.Quest],
            Lues().SelectMany(g => g.Links).Select(l => l.Kind));
    }

    [Fact]
    public void La_quete_ouverte_ne_se_propose_pas_elle_meme()
    {
        var groupes = Lues("https://papycha.fr/quete-votre-premier-dofus/");

        Assert.DoesNotContain(
            groupes.SelectMany(g => g.Links),
            l => l.Title.Contains("Un nouveau Dofus", StringComparison.Ordinal));
    }

    [Fact]
    public void La_barre_finale_et_l_ancre_ne_font_pas_deux_adresses()
    {
        // Le site sert la même page avec ou sans barre oblique finale : sans
        // cette réduction, la quête ouverte se proposerait à elle-même.
        var groupes = Lues("https://papycha.fr/quete-votre-premier-dofus#etape-3");

        Assert.Equal(2, QuestFollowUps.Count(groupes));
    }

    [Fact]
    public void Un_lien_repete_ne_compte_qu_une_fois()
    {
        // Le site range parfois la même quête sous deux objectifs. La lire deux
        // fois ferait croire à deux suites distinctes.
        var chain = new QuestChain
        {
            Next =
            [
                new QuestLink("Un nouveau Dofus ?", "https://papycha.fr/a/", QuestLinkKind.Quest)
                {
                    Objective = "Premier objectif",
                },
                new QuestLink("Un nouveau Dofus ?", "https://papycha.fr/a/", QuestLinkKind.Quest)
                {
                    Objective = "Second objectif",
                },
            ],
        };

        Assert.Equal(1, QuestFollowUps.Count(QuestFollowUps.Of(chain, null)));
    }

    [Fact]
    public void Une_colonne_vide_ne_rend_aucun_groupe()
    {
        // La fenêtre ne doit alors rien dessiner du tout, et surtout pas un
        // cadre vide sous la dernière étape.
        Assert.Empty(QuestFollowUps.Of(new QuestChain(), null));
        Assert.Empty(QuestFollowUps.Of(null, null));
        Assert.Equal(0, QuestFollowUps.Count(null));
    }
}
