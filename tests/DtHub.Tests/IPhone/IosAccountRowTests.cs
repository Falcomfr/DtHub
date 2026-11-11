using DtHub.App.ViewModels;
using DtHub.Core.Dofus;
using DtHub.Core.IPhone;
using DtHub.Core.Sessions;

namespace DtHub.Tests.IPhone;

/// <summary>An iPhone's account row, read from its window's life.</summary>
public class IosAccountRowTests
{
    private static readonly IPhoneDevice IPad = new("U1", "iPad de Léa", "27.2.1", "USB", "iPad");

    [Fact]
    public void Un_iPhone_branche_garde_sa_ligne_meme_sans_fenetre()
    {
        Assert.True(IosAccountRow.Shows("U1", new HashSet<string> { "U1" }, state: null));
    }

    [Fact]
    public void Un_iPhone_debranche_sans_fenetre_n_a_plus_de_ligne()
    {
        Assert.False(IosAccountRow.Shows("U1", new HashSet<string> { "U2" }, state: null));
    }

    [Fact]
    public void Un_iPhone_debranche_dont_la_fenetre_attend_garde_sa_ligne()
    {
        Assert.True(IosAccountRow.Shows("U1", new HashSet<string>(), IosWindowState.Unplugged));
    }

    [Theory]
    [InlineData(IosWindowState.Opening, InstanceActivity.Starting)]
    [InlineData(IosWindowState.Open, InstanceActivity.Running)]
    [InlineData(IosWindowState.Unplugged, InstanceActivity.Starting)]
    [InlineData(IosWindowState.Failed, InstanceActivity.Starting)]
    [InlineData(IosWindowState.Closed, InstanceActivity.Idle)]
    [InlineData(null, InstanceActivity.Idle)]
    public void Le_bouton_fermer_reste_tant_que_la_fenetre_attend(IosWindowState? state, InstanceActivity expected)
    {
        // Unplugged or failing, the window is still there and waits: the row
        // must still offer to close it. Given up, the play button opens it afresh.
        Assert.Equal(expected, IosAccountRow.Activity(state));
    }

    [Fact]
    public void La_ligne_dit_l_etat_de_la_fenetre()
    {
        Assert.Null(IosAccountRow.Status(null, gameMissing: false));
        Assert.Equal(("IosOpening", false), IosAccountRow.Status(IosWindowState.Opening, gameMissing: false));
        Assert.Equal(("IosOpen", false), IosAccountRow.Status(IosWindowState.Open, gameMissing: false));
        Assert.Equal(("IosUnplugged", true), IosAccountRow.Status(IosWindowState.Unplugged, gameMissing: false));
        Assert.Equal(("IosFailed", true), IosAccountRow.Status(IosWindowState.Failed, gameMissing: false));
        Assert.Equal(("IosFailed", true), IosAccountRow.Status(IosWindowState.Closed, gameMissing: false));
    }

    [Fact]
    public void Le_jeu_absent_se_dit_a_la_place_de_ouvert()
    {
        Assert.Equal(("IosGameMissing", true), IosAccountRow.Status(IosWindowState.Open, gameMissing: true));
    }

    [Fact]
    public void La_cle_est_celle_du_compte_iOS()
    {
        Assert.Equal("U1|0|ios", IosAccountRow.KeyOf("U1"));
    }

    [Fact]
    public void L_en_tete_d_un_iPad_dit_son_systeme_et_aucune_batterie()
    {
        var header = new DeviceGroupViewModel("U1", "U1");

        header.Update(IPad);

        Assert.True(header.IsIos);
        Assert.True(header.IsConnected);
        Assert.Equal("iPad de Léa", header.Name);
        Assert.Equal("iPadOS 27.2.1", header.StatusText);
        Assert.Equal("SuccessBrush", header.StatusBrushKey);
        Assert.False(header.HasBattery);
        Assert.False(header.IsLookingForGames);
        Assert.False(header.HasProblems);
    }

    [Fact]
    public void Un_appareil_trop_ancien_dit_pourquoi_sous_son_nom()
    {
        var header = new DeviceGroupViewModel("U1", "U1");

        header.Update(IPad with { IosVersion = "26.1" });
        header.SetIosProblem(null);

        Assert.True(header.HasProblems);
        Assert.Equal(DeviceGroupViewModel.TooOldHelp(IPad with { IosVersion = "26.1" }), header.Problems);
        Assert.Equal("WarningBrush", header.StatusBrushKey);
    }

    [Fact]
    public void Un_iPhone_debranche_dont_la_fenetre_attend_est_hors_ligne()
    {
        var header = new DeviceGroupViewModel("U1", "U1");
        header.Update(IPad);

        header.Unplugged();

        Assert.False(header.IsConnected);
        Assert.Equal("TextMutedBrush", header.StatusBrushKey);
    }

    [Fact]
    public void Un_en_tete_Android_n_est_pas_iOS()
    {
        Assert.False(new DeviceGroupViewModel("A1", "Pixel").IsIos);
    }

    [Fact]
    public void La_ligne_iOS_cache_les_reglages_Android_et_dit_son_etat()
    {
        var row = new InstanceRowViewModel(new DofusInstance
        {
            DeviceId = "U1",
            DeviceName = "iPad de Léa",
            UserId = 0,
            UserName = "iPad de Léa",
            PackageName = DofusInstance.IosPackage,
            Platform = DevicePlatform.Ios,
        });

        row.SetIosStatus(IosAccountRow.Status(IosWindowState.Unplugged, gameMissing: false));

        Assert.True(row.IsIos);
        Assert.Equal("débranché, en attente", row.StatusLine);
        Assert.Equal("WarningBrush", row.StatusLineBrushKey);
    }
}
