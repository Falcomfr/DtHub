using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// The fragments used here are the ones the site actually renders,
/// captured from three quests of different types. Copying them in
/// rather than fetching them keeps the tests off the network, as the
/// repository requires.
/// </summary>
public class QuestPageParserTests
{
    /// <summary>
    /// Quest in the middle of a chain, with a success and a finality.
    /// </summary>
    private const string IntroDragonAstrub = """
            <section class="pqa-quest-intro wp-block-papycha-quest-intro" data-pqa-post="33878">
            <dl class="pqa-quest-intro__facts">
            <div class="pqa-quest-intro__fact--successes">
            <dt>Succès associé</dt>
            <dd data-pqa-fit>
            Devenir une légende                    </dd>
            </div>
            <div class="pqa-quest-intro__fact--finality">
            <dt>Finalité</dt>
            <dd data-pqa-fit>Avoir le Dofus Argenté</dd>
            </div>
            <div class="pqa-quest-intro__fact--step">
            <dt>Progression</dt>
            <dd data-pqa-fit>Étape 3/4</dd>
            </div>
            </dl>
            <dl class="pqa-quest-intro__taxonomies" aria-label="Classement de la quete">
            <div><dt>Type</dt><dd>Principale</dd></div>                                </dl>
            <div class="pqa-quest-intro__start">
            <p class="pqa-quest-intro__eyebrow">Départ de la quête</p>
            <p class="pqa-quest-intro__lead" data-pqa-fit>
            Cette quête se lance automatiquement après avoir terminé
            la quête                         <a class="pqa-quest-intro__quest-link" href="https://papycha.fr/quete-dans-les-pas-du-chevalier-de-lautomne/">Dans les pas du Chevalier de l’Automne</a>.
            </p>
            </div>
            </section>
            """;

    /// <summary>Progression block of the same quest.</summary>
    private const string NavDragonAstrub = """
            <nav class="pqt-progress" aria-label="Progression de la quête">
            <section class="pqt-progress__column pqt-progress__column--previous">
            <h3>Quêtes et jalons précédents</h3>
            <div class="pqt-progress__items"><a class="pqt-progress__item pqt-progress__item--quest" href="https://papycha.fr/quete-dans-les-pas-du-chevalier-de-lautomne/"><small>Quête précédente</small><strong>Dans les pas du Chevalier de l’Automne</strong></a></div>
            </section>
            <section class="pqt-progress__column pqt-progress__column--next">
            <h3>Quêtes et jalons suivants</h3>
            <div class="pqt-progress__items"><a class="pqt-progress__item pqt-progress__item--success" href="https://papycha.fr/succes/?pqt_success=success:cards.devenir-une-legende#succes-selectionne"><small>Succès validé</small><strong>[Succès] Devenir une légende</strong></a><section class="pqt-progress__objective-group"><header class="pqt-progress__objective-title"><span>Objectif :</span><strong>Dofus Cawotte obtenu</strong></header><div class="pqt-progress__objective-quests"><a class="pqt-progress__objective-quest pqt-progress__objective-quest--quest" href="https://papycha.fr/quete-votre-premier-dofus/"><strong>Un nouveau Dofus ?</strong></a></div></section><section class="pqt-progress__objective-group"><header class="pqt-progress__objective-title"><span>Objectif :</span><strong>Suite du parcours</strong></header><div class="pqt-progress__objective-quests"><a class="pqt-progress__objective-quest pqt-progress__objective-quest--quest" href="https://papycha.fr/quete-la-decouverte-dun-vaste-monde/"><strong>La découverte d’un vaste monde !</strong></a></div></section></div>
            </section>
            </nav>
            """;

    /// <summary>
    /// Alignment quest: long chain, but no associated success.
    /// </summary>
    private const string IntroAlignement = """
            <section class="pqa-quest-intro wp-block-papycha-quest-intro" data-pqa-post="15371">
            <dl class="pqa-quest-intro__facts">
            <div class="pqa-quest-intro__fact--finality">
            <dt>Finalité</dt>
            <dd data-pqa-fit>Apprentissage : Adepte des Écrits</dd>
            </div>
            <div class="pqa-quest-intro__fact--step">
            <dt>Progression</dt>
            <dd data-pqa-fit>Étape 1/61</dd>
            </div>
            </dl>
            <dl class="pqa-quest-intro__taxonomies" aria-label="Classement de la quete">
            <div><dt>Type</dt><dd>Alignement Bonta</dd></div>                                </dl>
            <div class="pqa-quest-intro__section pqa-quest-intro__requirements">
            <div class="pqa-quest-intro__requirements-grid">
            <div class="pqa-quest-intro__subsection pqa-quest-intro__checklist" data-pqa-checklist="prerequisites">
            <h4>Prérequis</h4>
            <ul>
            <li>
            <input type="checkbox" id="pqa-15371-prerequisitese49b411852915526" data-pqa-key="prerequisites:e49b411852915526">
            <label for="pqa-15371-prerequisitese49b411852915526" data-pqa-fit>
            Alignement bontarien 20 minimum                                            </label>
            </li>
            </ul>
            </div>
            </div>
            </div>
            <div class="pqa-quest-intro__start">
            <p class="pqa-quest-intro__eyebrow">Départ de la quête</p>
            <p class="pqa-quest-intro__lead" data-pqa-fit>
            Pour lancer cette quête,
            rendez-vous en
            <span class="pqa-quest-intro__position">
            <a class="pqa-quest-intro__copy" href="https://papycha.fr/carte/#x=-34&#038;y=-57&#038;z=6" data-bi-map="[-34,-57]" aria-label="Voir la position [-34,-57] sur la carte"><strong>[-34,-57]</strong></a>                        </span>                                         dans <strong>Centre-ville</strong>                     et                     parlez à <strong>Elviana Tirips</strong>.
            </p>
            </div>
            </section>
            """;

    [Fact]
    public void Le_bloc_d_introduction_livre_le_succes_la_finalite_et_le_type()
    {
        var facts = QuestPageParser.ParseFacts(IntroDragonAstrub);

        Assert.Equal("Devenir une légende", facts.Success);
        Assert.Equal("Avoir le Dofus Argenté", facts.Finality);
        Assert.Equal("Principale", facts.Type);
    }

    [Fact]
    public void La_progression_se_lit_en_rang_et_en_total()
    {
        var facts = QuestPageParser.ParseFacts(IntroDragonAstrub);

        Assert.Equal(3, facts.StepNumber);
        Assert.Equal(4, facts.StepCount);
        Assert.True(facts.HasChain);
        Assert.Equal("Étape 3/4", facts.StepText);
    }

    [Fact]
    public void Une_quete_sans_succes_garde_sa_progression()
    {
        // Alignment quests have no associated success but do form a
        // chain, sixty-one quests long here.
        var facts = QuestPageParser.ParseFacts(IntroAlignement);

        Assert.Null(facts.Success);
        Assert.Equal(1, facts.StepNumber);
        Assert.Equal(61, facts.StepCount);
        Assert.Equal("Apprentissage : Adepte des Écrits", facts.Finality);
    }

    [Fact]
    public void La_quete_precedente_est_lue_dans_la_colonne_de_gauche()
    {
        var chain = QuestPageParser.ParseChain(NavDragonAstrub);

        var precedente = chain.PreviousQuest;

        Assert.NotNull(precedente);
        Assert.Equal("Dans les pas du Chevalier de l’Automne", precedente.Title);
        Assert.Equal(
            "https://papycha.fr/quete-dans-les-pas-du-chevalier-de-lautomne/",
            precedente.Url);
    }

    [Fact]
    public void Les_suites_sont_distinguees_du_succes_valide()
    {
        // The right-hand column mixes the unlocked success with the
        // quests it opens: offering the success as the next quest would
        // send the user to a listing page.
        var chain = QuestPageParser.ParseChain(NavDragonAstrub);

        Assert.Contains(chain.Next, l => l.Kind == QuestLinkKind.Success);

        var suivante = chain.NextQuest;

        Assert.NotNull(suivante);
        Assert.Equal("Un nouveau Dofus ?", suivante.Title);
        Assert.Equal(QuestLinkKind.Quest, suivante.Kind);
    }

    /// <summary>
    /// The button shows only one quest: when the column names several,
    /// singling one out would misrepresent what the site publishes.
    /// Observed across the 782 guides: 79 "next" columns name more than
    /// one.
    /// </summary>
    [Fact]
    public void Une_colonne_qui_nomme_plusieurs_quetes_n_en_designe_aucune()
    {
        var chain = QuestPageParser.ParseChain(NavDragonAstrub);

        // Two objectives, two quests: "Un nouveau Dofus ?" and "La
        // découverte d'un vaste monde !".
        Assert.Equal(2, chain.Next.Count(l => l.Kind == QuestLinkKind.Quest));

        Assert.NotNull(chain.NextQuest);
        Assert.Null(chain.OnlyNextQuest);
    }

    [Fact]
    public void Une_colonne_qui_n_en_nomme_qu_une_la_designe()
    {
        var chain = QuestPageParser.ParseChain(NavDragonAstrub);

        Assert.Equal(
            "https://papycha.fr/quete-dans-les-pas-du-chevalier-de-lautomne/",
            chain.OnlyPreviousQuest?.Url);
    }

    /// <summary>A validated success is not a next quest.</summary>
    [Fact]
    public void Un_succes_seul_ne_fait_pas_une_suivante()
    {
        const string html = """
            <nav class="pqt-progress">
            <section class="pqt-progress__column pqt-progress__column--next">
            <div class="pqt-progress__items"><a class="pqt-progress__item pqt-progress__item--success"
            href="https://papycha.fr/succes/?pqt_success=x"><strong>[Succès] Un nouveau départ</strong></a></div>
            </section>
            </nav>
            """;

        Assert.Null(QuestPageParser.ParseChain(html).OnlyNextQuest);
    }

    [Fact]
    public void Les_apostrophes_typographiques_sont_decodees()
    {
        // The site serves HTML entities: without decoding, the title
        // would display "l&#8217;Automne".
        var chain = QuestPageParser.ParseChain(NavDragonAstrub);

        Assert.DoesNotContain("&#", chain.PreviousQuest!.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("&amp;", chain.PreviousQuest.Title, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p>une page sans le moindre bloc attendu</p>")]
    public void Une_page_sans_les_blocs_attendus_ne_fait_rien_echouer(string? html)
    {
        // The site can change its blocks without warning us: the window
        // must then display the page without a step bar, not close.
        var facts = QuestPageParser.ParseFacts(html);
        var chain = QuestPageParser.ParseChain(html);

        Assert.Null(facts.Success);
        Assert.False(facts.HasChain);
        Assert.Empty(facts.StepText);
        Assert.Empty(chain.Previous);
        Assert.Empty(chain.Next);
        Assert.Null(chain.NextQuest);
    }

    [Fact]
    public void Une_progression_illisible_vaut_pas_de_chaine_du_tout()
    {
        // Better to announce nothing than to announce a made-up
        // position.
        var facts = QuestPageParser.ParseFacts(
            """<div class="pqa-quest-intro__fact--step"><dt>Progression</dt><dd>Étape finale</dd></div>""");

        Assert.False(facts.HasChain);
        Assert.Equal(0, facts.StepNumber);
    }
}
