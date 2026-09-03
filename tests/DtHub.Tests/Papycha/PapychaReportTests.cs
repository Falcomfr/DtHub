using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// Ce que l'application écrit à la place du lecteur dans le formulaire de
/// signalement du site : le repère d'étape, et rien d'autre.
/// </summary>
public class PapychaReportTests
{
    [Fact]
    public void Sans_etape_le_champ_reste_libre()
    {
        // Une rubrique ou une carte n'a pas d'étape : un repère inventé y
        // vaudrait moins que le champ laissé vide.
        Assert.Equal(string.Empty, PapychaReport.Location(-1, 0, "peu importe"));
        Assert.Equal(string.Empty, PapychaReport.Location(0, 0, "peu importe"));
        Assert.Equal(string.Empty, PapychaReport.Location(4, 3, "peu importe"));
    }

    [Fact]
    public void L_etape_est_comptee_a_partir_de_un()
    {
        // L'index part de zéro chez nous, la page dit « Étape 3 / 12 ».
        Assert.StartsWith("Étape 3 / 12", PapychaReport.Location(2, 12, null), StringComparison.Ordinal);
    }

    [Fact]
    public void Le_debut_de_l_etape_est_cite()
    {
        var pris = PapychaReport.Location(0, 4, "Parlez à Gustave Chapal, à Astrub.");

        Assert.Equal("Étape 1 / 4 : « Parlez à Gustave Chapal, à Astrub. »", pris);
    }

    [Fact]
    public void Une_etape_vide_ne_laisse_que_le_repere()
    {
        Assert.Equal("Étape 2 / 5", PapychaReport.Location(1, 5, "   "));
        Assert.Equal("Étape 2 / 5", PapychaReport.Location(1, 5, null));
    }

    [Fact]
    public void Les_blancs_de_la_page_sont_reduits()
    {
        // Le texte vient du HTML : sauts de ligne et indentation compris.
        var pris = PapychaReport.Location(0, 2, "  Parlez\n   à   Gustave\tChapal  ");

        Assert.Equal("Étape 1 / 2 : « Parlez à Gustave Chapal »", pris);
    }

    [Fact]
    public void Une_longue_etape_est_coupee_sur_un_mot_entier()
    {
        var texte = string.Join(' ', Enumerable.Repeat("mot", 200));

        var pris = PapychaReport.Location(0, 3, texte);

        Assert.True(pris.Length <= PapychaReport.MaxLocationLength, pris);
        Assert.EndsWith("… »", pris, StringComparison.Ordinal);

        // Coupé entre deux mots : la citation doit se retrouver telle quelle
        // dans la page en la cherchant.
        Assert.DoesNotContain("mo… ", pris, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_seul_mot_interminable_laisse_le_repere_seul()
    {
        // Rien à couper proprement, et la limite du champ prime : mieux vaut le
        // repère seul qu'une citation tronquée par le navigateur.
        var pris = PapychaReport.Location(0, 3, new string('x', 400));

        Assert.True(pris.Length <= PapychaReport.MaxLocationLength, pris);
        Assert.StartsWith("Étape 1 / 3", pris, StringComparison.Ordinal);
    }
}
