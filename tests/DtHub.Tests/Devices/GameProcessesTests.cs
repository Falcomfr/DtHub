using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class GameProcessesTests
{
    // Read on the 13T Pro on 2026-10-26, two accounts open.
    private const string Sortie =
        "USER         NAME                       \n"
        + "u0_a475      com.ankama.dofustouch\n"
        + "u999_a475    com.ankama.dofustouch\n"
        + "u0_a475      com.ankama.dofustouch:webview\n"
        + "root         init\n";

    [Fact]
    public void Chaque_compte_android_qui_fait_tourner_le_jeu_est_lu()
    {
        Assert.Equal([0, 999], GameProcesses.UsersRunning(Sortie, "com.ankama.dofustouch").Order());
    }

    [Fact]
    public void Un_jeu_mort_n_est_plus_dans_la_liste()
    {
        // The game killed for the account 999: only the main profile's is left.
        var sortie = Sortie.Replace("u999_a475    com.ankama.dofustouch\n", string.Empty, StringComparison.Ordinal);

        Assert.Equal([0], GameProcesses.UsersRunning(sortie, "com.ankama.dofustouch"));
    }

    [Fact]
    public void Un_autre_processus_du_meme_paquet_ne_compte_pas()
    {
        Assert.Empty(GameProcesses.UsersRunning(
            "u10_a475      com.ankama.dofustouch:webview\n", "com.ankama.dofustouch"));
    }
}
