using DtHub.Core.Almanax;

namespace DtHub.Tests.Almanax;

public class MonthGridTests
{
    [Fact]
    public void La_grille_fait_toujours_six_semaines()
    {
        // Février d'une année non bissextile commençant un lundi tient en
        // quatre lignes, août 2026 en six. Une grille qui s'ajuste ferait
        // sauter la hauteur de la fenêtre d'un mois à l'autre.
        Assert.Equal(42, MonthGrid.For(2026, 2, DayOfWeek.Monday).Count);
        Assert.Equal(42, MonthGrid.For(2026, 8, DayOfWeek.Monday).Count);
    }

    [Fact]
    public void La_grille_commence_au_premier_jour_de_la_semaine()
    {
        var lundi = MonthGrid.For(2026, 9, DayOfWeek.Monday);
        var dimanche = MonthGrid.For(2026, 9, DayOfWeek.Sunday);

        Assert.Equal(DayOfWeek.Monday, lundi[0].DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, dimanche[0].DayOfWeek);
    }

    [Fact]
    public void Le_premier_du_mois_tombe_dans_la_premiere_semaine()
    {
        // Septembre 2026 commence un mardi : en semaine commençant lundi, il
        // est en deuxième case, précédé du 31 août.
        var days = MonthGrid.For(2026, 9, DayOfWeek.Monday);

        Assert.Equal(new DateOnly(2026, 8, 31), days[0]);
        Assert.Equal(new DateOnly(2026, 9, 1), days[1]);
    }

    [Fact]
    public void Un_mois_commencant_le_premier_jour_ne_deborde_pas_en_tete()
    {
        // Juin 2026 commence un lundi : aucune case du mois précédent.
        var days = MonthGrid.For(2026, 6, DayOfWeek.Monday);

        Assert.Equal(new DateOnly(2026, 6, 1), days[0]);
    }

    [Fact]
    public void Les_jours_se_suivent_sans_trou_ni_doublon()
    {
        var days = MonthGrid.For(2026, 2, DayOfWeek.Monday);

        for (var i = 1; i < days.Count; i++)
        {
            Assert.Equal(days[i - 1].AddDays(1), days[i]);
        }
    }

    [Fact]
    public void Une_annee_bissextile_montre_bien_le_vingt_neuf_fevrier()
    {
        Assert.Contains(new DateOnly(2028, 2, 29), MonthGrid.For(2028, 2, DayOfWeek.Monday));
    }

    [Fact]
    public void Un_mois_de_bord_d_annee_traverse_le_changement_d_annee()
    {
        var decembre = MonthGrid.For(2026, 12, DayOfWeek.Monday);

        Assert.Contains(new DateOnly(2027, 1, 1), decembre);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, DayOfWeek.Sunday)]
    [InlineData(DayOfWeek.Sunday, DayOfWeek.Saturday)]
    [InlineData(DayOfWeek.Saturday, DayOfWeek.Friday)]
    public void L_entete_part_du_premier_jour_et_fait_le_tour(DayOfWeek first, DayOfWeek last)
    {
        var header = MonthGrid.Header(first);

        Assert.Equal(7, header.Count);
        Assert.Equal(first, header[0]);
        Assert.Equal(last, header[6]);
        Assert.Equal(7, header.Distinct().Count());
    }
}
