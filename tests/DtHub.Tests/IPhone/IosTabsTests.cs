using DtHub.Core.IPhone;

namespace DtHub.Tests.IPhone;

public class IosTabsTests
{
    [Fact]
    public void Un_iPhone_ouvert_garde_son_onglet_sans_attendre()
    {
        var (shown, waiting) = IosTabs.Split([("k", IosWindowState.Open, true)]);

        Assert.Equal(["k"], shown);
        Assert.Empty(waiting);
    }

    [Fact]
    public void Un_iPhone_qui_demarre_montre_sa_fenetre()
    {
        var (shown, waiting) = IosTabs.Split([("k", IosWindowState.Opening, true)]);

        Assert.Equal(["k"], shown);
        Assert.Empty(waiting);
    }

    [Fact]
    public void Un_iPhone_debranche_garde_son_onglet_en_attente()
    {
        var (shown, waiting) = IosTabs.Split([("k", IosWindowState.Unplugged, true)]);

        Assert.Equal(["k"], shown);
        Assert.Equal(["k"], waiting);
    }

    [Fact]
    public void Un_iPhone_en_echec_attend_son_nouvel_essai()
    {
        var (shown, waiting) = IosTabs.Split([("k", IosWindowState.Failed, true)]);

        Assert.Equal(["k"], shown);
        Assert.Equal(["k"], waiting);
    }

    [Fact]
    public void Une_fenetre_en_cours_de_reouverture_laisse_son_onglet_attendre_la_nouvelle()
    {
        var (shown, waiting) = IosTabs.Split([("k", IosWindowState.Opening, false)]);

        Assert.Empty(shown);
        Assert.Equal(["k"], waiting);
    }

    [Fact]
    public void Une_fenetre_fermee_ne_garde_pas_son_onglet()
    {
        var (shown, waiting) = IosTabs.Split([("k", IosWindowState.Closed, false), ("j", null, false)]);

        Assert.Empty(shown);
        Assert.Empty(waiting);
    }

    [Fact]
    public void Une_fenetre_abandonnee_encore_affichee_garde_son_onglet()
    {
        var (shown, waiting) = IosTabs.Split([("k", IosWindowState.Closed, true)]);

        Assert.Equal(["k"], shown);
        Assert.Empty(waiting);
    }
}
