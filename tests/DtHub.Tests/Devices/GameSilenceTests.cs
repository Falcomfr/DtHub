using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// A game that plays nothing any more on the phone, whatever came before.
/// A player on a Samsung lost the sound of both his accounts on 2026-10-22,
/// the capture still running, and only a restart of the phone brought it
/// back.
/// </summary>
public class GameSilenceTests
{
    private const string Phone = "R5CY83XVEDT";

    private static readonly GameSound Playing = new(2, 2, true);

    private static readonly GameSound Silent = new(2, 0, true);

    private static readonly GameSound NoGame = new(0, 0, false);

    private static GameSilence SilentFor(int readings, bool carried = true)
    {
        var watch = new GameSilence();

        for (var i = 0; i < readings; i++)
        {
            _ = watch.Read(Phone, Silent, carried);
        }

        return watch;
    }

    [Fact]
    public void Le_silence_est_signale_au_troisieme_releve_muet_quand_le_son_va_au_pc()
    {
        var watch = new GameSilence();

        Assert.False(watch.Read(Phone, Silent, carried: true));
        Assert.False(watch.Read(Phone, Silent, carried: true));
        Assert.False(watch.Shows(Phone));

        Assert.True(watch.Read(Phone, Silent, carried: true));
        Assert.True(watch.Shows(Phone));

        // Once per silence: the next silent reading does not start it again.
        Assert.False(watch.Read(Phone, Silent, carried: true));
        Assert.True(watch.Shows(Phone));
    }

    [Fact]
    public void Sans_son_vers_le_pc_le_silence_est_ecrit_mais_pas_affiche()
    {
        // The log still wants it, the player has nothing to miss on the PC.
        var watch = new GameSilence();
        _ = watch.Read(Phone, Silent, carried: false);
        _ = watch.Read(Phone, Silent, carried: false);

        Assert.True(watch.Read(Phone, Silent, carried: false));
        Assert.False(watch.Shows(Phone));
    }

    [Fact]
    public void Le_son_coupe_vers_le_pc_en_cours_de_silence_retire_l_alerte()
    {
        var watch = SilentFor(3);

        Assert.False(watch.Read(Phone, Silent, carried: false));
        Assert.False(watch.Shows(Phone));
    }

    [Fact]
    public void Le_son_revenu_efface_l_alerte_et_le_silence_suivant_est_signale_de_nouveau()
    {
        var watch = SilentFor(3);

        Assert.False(watch.Read(Phone, Playing, carried: true));
        Assert.False(watch.Shows(Phone));
        Assert.False(watch.Lasts(Phone));

        _ = watch.Read(Phone, Silent, carried: true);
        _ = watch.Read(Phone, Silent, carried: true);
        Assert.True(watch.Read(Phone, Silent, carried: true));
    }

    [Fact]
    public void Sans_jeu_ouvert_l_alerte_tombe()
    {
        var watch = SilentFor(3);

        Assert.False(watch.Read(Phone, NoGame, carried: true));
        Assert.False(watch.Shows(Phone));
        Assert.False(watch.Lasts(Phone));
    }

    [Fact]
    public void Un_releve_qui_entend_le_jeu_remet_le_compte_a_zero()
    {
        // A loading screen is silent too: two quiet readings, then the game.
        var watch = SilentFor(2);
        _ = watch.Read(Phone, Playing, carried: true);

        Assert.False(watch.Read(Phone, Silent, carried: true));
        Assert.False(watch.Read(Phone, Silent, carried: true));
        Assert.True(watch.Read(Phone, Silent, carried: true));
    }

    [Fact]
    public void Un_autre_telephone_n_est_pas_touche()
    {
        var watch = SilentFor(3);

        Assert.False(watch.Shows("autre"));
        Assert.False(watch.Read("autre", Silent, carried: true));
        Assert.True(watch.Shows(Phone));
    }
}
