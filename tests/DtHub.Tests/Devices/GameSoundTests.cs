using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// Whether the game is playing sound, and whether scrcpy is taking it for
/// the PC. The player lost the sound now and then, on the PC and on the
/// phone alike, and relaunching DT Hub brought it back (2026-10-16): nothing
/// recorded the moment it went.
/// </summary>
public class GameSoundTests
{
    // Read on the 13T Pro on 2026-10-17, the game playing its music and a
    // window carrying the sound: the game's two players, scrcpy's capture
    // under the shell's uid, each port listed twice by the policy.
    private const string Playing = """
        10475
        players
              1. Port ID: 189; Session ID: 1257; uid 2000; State: Active
              1. Port ID: 194; Session ID: 1265; uid 10475; State: Active
              2. Port ID: 195; Session ID: 1273; uid 10475; State: Active
              id 1: Port ID: 194; Session ID: 1265; uid 10475; State: Active
              id 1: Port ID: 195; Session ID: 1273; uid 10475; State: Active
        """;

    [Fact]
    public void Le_jeu_qui_joue_et_la_capture_se_lisent()
    {
        var sound = GameSound.Parse(Playing)!;

        Assert.Equal(1, sound.GameProcesses);
        Assert.Equal(2, sound.GamePlayers);
        Assert.True(sound.Captured);
        Assert.False(sound.IsSilent);
    }

    [Fact]
    public void Un_jeu_ouvert_sans_aucun_lecteur_est_muet()
    {
        var sound = GameSound.Parse("""
            10475
            players
                  1. Port ID: 189; Session ID: 1257; uid 2000; State: Active
            """)!;

        Assert.Equal(0, sound.GamePlayers);
        Assert.True(sound.IsSilent);
    }

    [Fact]
    public void Sans_jeu_ouvert_rien_n_est_muet()
    {
        var sound = GameSound.Parse("players\n")!;

        Assert.Equal(0, sound.GameProcesses);
        Assert.False(sound.IsSilent);
        Assert.False(sound.Captured);
    }

    [Fact]
    public void Un_jeu_dans_un_autre_profil_android_compte_par_son_uid()
    {
        // Profile 10: the uid carries the user, 10 x 100000 + the app's id.
        var sound = GameSound.Parse("""
            1010475
            players
                  1. Port ID: 7; Session ID: 9; uid 1010475; State: Active
            """)!;

        Assert.Equal(1, sound.GamePlayers);
    }

    [Fact]
    public void Une_reponse_sans_la_liste_ne_dit_rien()
    {
        Assert.Null(GameSound.Parse("sh: dumpsys: not found"));
        Assert.Null(GameSound.Parse(null));
    }
}
