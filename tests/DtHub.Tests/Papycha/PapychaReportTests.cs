using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// What the application writes in place of the reader in the site's
/// report form: the zone and the quest, and nothing else.
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
        // A dungeon or path page does not belong to any category on
        // the site: its title is then all there is to say about it.
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
        // A made-up landmark would be worth less than the field left
        // empty.
        Assert.Equal(string.Empty, PapychaReport.Location(null, null));
        Assert.Equal(string.Empty, PapychaReport.Location(" ", "  "));
    }

    [Fact]
    public void Les_blancs_du_bandeau_sont_reduits()
    {
        // Titles come from the site: line breaks and indentation
        // included.
        Assert.Equal(
            "Île de Frigost  ›  Sans ma barbe, quelle barbe",
            PapychaReport.Location(" Île de\nFrigost ", "Sans ma  barbe,\tquelle barbe"));
    }

    [Fact]
    public void Le_champ_du_site_n_est_jamais_depasse()
    {
        // This case should not happen, a zone name and a quest title
        // fitting well under the limit; it is a safeguard against
        // input that the browser would silently truncate.
        var pris = PapychaReport.Location(new string('z', 200), new string('q', 200));

        Assert.True(pris.Length <= PapychaReport.MaxLocationLength, pris);
    }

    [Fact]
    public void Le_succes_suit_la_quete_entre_parentheses()
    {
        Assert.Equal(
            "Astrub  ›  La découverte d'un destin (Un nouveau départ)",
            PapychaReport.Location("Astrub", "La découverte d'un destin", "Un nouveau départ"));
    }

    [Fact]
    public void Sans_succes_la_parenthese_ne_parait_pas()
    {
        // A path or a dungeon has no achievement: an empty
        // parenthesis there would be worth less than nothing.
        Assert.Equal("Astrub  ›  Antiroyaliste", PapychaReport.Location("Astrub", "Antiroyaliste", null));
        Assert.Equal("Astrub  ›  Antiroyaliste", PapychaReport.Location("Astrub", "Antiroyaliste", "  "));
    }

    [Fact]
    public void Le_succes_accompagne_la_quete_meme_sans_zone()
    {
        Assert.Equal(
            "Antiroyaliste (Halte au péage)",
            PapychaReport.Location(null, "Antiroyaliste", "Halte au péage"));
    }
}
