using DtHub.Core.Hotkeys;

namespace DtHub.Tests.Hotkeys;

/// <summary>
/// La règle qui empêche les raccourcis d'être globaux. C'est une promesse
/// affichée dans le README, et elle n'avait aucun filet : une régression aurait
/// confisqué Ctrl+Tab et Ctrl+R au navigateur sans que rien ne rougisse.
/// </summary>
public class HotkeyScopeTests
{
    private static readonly SessionWindow[] Deux =
    [
        new(Handle: 1000, ProcessId: 42),
        new(Handle: 2000, ProcessId: 43),
    ];

    [Fact]
    public void Une_fenetre_de_jeu_au_premier_plan_arme_les_raccourcis()
    {
        Assert.True(HotkeyScope.Holds(1000, owner: 42, Deux, ours: false));
    }

    [Fact]
    public void Une_fenetre_etrangere_les_desarme()
    {
        // Le cas qui compte : le navigateur au premier plan doit récupérer
        // Ctrl+Tab.
        Assert.False(HotkeyScope.Holds(9999, owner: 777, Deux, ours: false));
    }

    [Fact]
    public void Le_processus_suffit_quand_le_handle_n_est_pas_encore_resolu()
    {
        // Une session fraîchement rouverte n'a pas encore son handle : sans
        // cette voie, les raccourcis restaient éteints jusqu'à ce qu'on clique
        // ailleurs puis de nouveau sur une fenêtre de jeu.
        Assert.True(HotkeyScope.Holds(4321, owner: 43, Deux, ours: false));
    }

    [Fact]
    public void Un_processus_inconnu_ne_vaut_pas_reconnaissance()
    {
        // Zéro veut dire « je n'ai pas pu savoir ». Le traiter comme une valeur
        // ordinaire armerait les raccourcis sur n'importe quelle fenêtre dont
        // le processus n'est pas lisible.
        Assert.False(HotkeyScope.Holds(4321, owner: 0, [new(Handle: 1000, ProcessId: 0)], ours: false));
    }

    [Fact]
    public void Une_fenetre_a_nous_arme_les_raccourcis_sans_aucune_session()
    {
        // Le configurateur, les guides, une page liée, ou le cadre à onglets.
        // Ce dernier manquait : cliquer sur la barre d'onglets éteignait les
        // douze raccourcis, Ctrl+P compris.
        Assert.True(HotkeyScope.Holds(5555, owner: 0, [], ours: true));
    }

    [Fact]
    public void Sans_rien_d_ouvert_ni_de_reconnu_ils_restent_en_veille()
    {
        Assert.False(HotkeyScope.Holds(5555, owner: 12, [], ours: false));
    }
}
