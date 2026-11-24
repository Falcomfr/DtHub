using DtHub.App.ViewModels;
using DtHub.Core.Android;
using DtHub.Core.Dofus;

namespace DtHub.Tests.App;

/// <summary>
/// The Samsung game optimizing section of an account's settings: only the
/// original game on the main profile is drawn blurry by it (D237).
/// </summary>
public class GameOptimizingRowTests
{
    private static InstanceRowViewModel Row(int userId = 0, string package = DofusPackages.DofusTouch) =>
        new(new DofusInstance
        {
            DeviceId = "R5CW612EFBT",
            DeviceName = "samsung SM-G990B2",
            UserId = userId,
            UserName = "Principal",
            PackageName = package,
        });

    [Fact]
    public void L_original_du_profil_principal_propose_de_couper_GOS()
    {
        var row = Row();
        row.GameOptimizing = GameOptimizingState.On;

        Assert.True(row.OffersGameOptimizingOff);
        Assert.False(row.OffersGameOptimizingOn);
        Assert.True(row.HasGameOptimizing);
    }

    [Fact]
    public void GOS_coupe_propose_de_le_reactiver()
    {
        var row = Row();
        row.GameOptimizing = GameOptimizingState.Off;

        Assert.True(row.OffersGameOptimizingOn);
        Assert.False(row.OffersGameOptimizingOff);
    }

    [Theory]
    [InlineData(0, "com.ankama.dofustouch.clone1")]
    [InlineData(12, "com.ankama.dofustouch")]
    public void Une_copie_ou_un_autre_profil_ne_voit_rien(int userId, string package)
    {
        var row = Row(userId, package);
        row.GameOptimizing = GameOptimizingState.On;

        Assert.False(row.HasGameOptimizing);
    }

    [Fact]
    public void Un_etat_inconnu_ne_montre_rien()
    {
        var row = Row();
        Assert.False(row.HasGameOptimizing);

        row.GameOptimizing = GameOptimizingState.Absent;
        Assert.False(row.HasGameOptimizing);
    }

    [Fact]
    public void Le_bouton_demande_la_coupure()
    {
        var row = Row();
        var raised = 0;
        row.GameOptimizingRequested += (_, _) => raised++;

        row.TurnGameOptimizingOffCommand.Execute(null);

        Assert.Equal(1, raised);
        Assert.False(row.RequestedGameOptimizing);
    }
}
