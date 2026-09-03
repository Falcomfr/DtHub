using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// Ce que l'application écrit à la place du lecteur dans le formulaire de
/// signalement du site : la zone et la quête, et rien d'autre.
/// </summary>
public class PapychaReportTests
{
    [Fact]
    public void La_zone_precede_la_quete()
    {
        Assert.Equal(
            "Île de Frigost  ›  Antiroyaliste",
            PapychaReport.Location("Île de Frigost", "Antiroyaliste"));
    }

    [Fact]
    public void Sans_zone_la_quete_suffit()
    {
        // Une page de donjon ou de chemin n'appartient à aucune rubrique du
        // site : son titre est alors tout ce qu'on sait en dire.
        Assert.Equal("Antiroyaliste", PapychaReport.Location(null, "Antiroyaliste"));
        Assert.Equal("Antiroyaliste", PapychaReport.Location("   ", "Antiroyaliste"));
    }

    [Fact]
    public void Sans_quete_la_zone_part_seule()
    {
        Assert.Equal("Île de Frigost", PapychaReport.Location("Île de Frigost", null));
    }

    [Fact]
    public void Sans_rien_le_champ_reste_libre()
    {
        // Un repère inventé vaudrait moins que le champ laissé vide.
        Assert.Equal(string.Empty, PapychaReport.Location(null, null));
        Assert.Equal(string.Empty, PapychaReport.Location(" ", "  "));
    }

    [Fact]
    public void Les_blancs_du_bandeau_sont_reduits()
    {
        // Les titres viennent du site : sauts de ligne et indentation compris.
        Assert.Equal(
            "Île de Frigost  ›  Sans ma barbe, quelle barbe",
            PapychaReport.Location(" Île de\nFrigost ", "Sans ma  barbe,\tquelle barbe"));
    }

    [Fact]
    public void Le_champ_du_site_n_est_jamais_depasse()
    {
        // Le cas ne devrait pas se produire, un nom de zone et un titre de quête
        // tenant très en deçà ; c'est un garde-fou contre une saisie que le
        // navigateur tronquerait en silence.
        var pris = PapychaReport.Location(new string('z', 200), new string('q', 200));

        Assert.True(pris.Length <= PapychaReport.MaxLocationLength, pris);
    }
}
