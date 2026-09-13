using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;

namespace DtHub.Tests.Dofus;

public class GamePresenceReadingTests
{
    private static AndroidDevice Phone(string id, bool connected = true) => new()
    {
        Id = id,
        Serial = $"{id}:5555",
        State = connected ? AdbDeviceState.Device : AdbDeviceState.Offline,
    };

    private static DofusInstance Account(string deviceId, int userId = 0) => new()
    {
        DeviceId = deviceId,
        DeviceName = "Un téléphone",
        UserId = userId,
        UserName = "Principal",
        PackageName = "jeu",
    };

    private static Dictionary<string, GamePresence> Nothing() => new(StringComparer.Ordinal);

    [Fact]
    public void Une_liste_absente_ne_conclut_rien_sur_un_telephone_inconnu()
    {
        // The heart of the defect: the search has not answered yet, so
        // nothing whatsoever may be read into the silence.
        var read = GamePresenceReading.After(Nothing(), [Phone("tel")], instances: null);

        Assert.Equal(GamePresence.Unknown, read["tel"]);
    }

    [Fact]
    public void Une_liste_absente_laisse_intact_le_verdict_deja_rendu()
    {
        var known = new Dictionary<string, GamePresence>(StringComparer.Ordinal)
        {
            ["tel"] = GamePresence.Present,
        };

        var read = GamePresenceReading.After(known, [Phone("tel")], instances: null);

        Assert.Equal(GamePresence.Present, read["tel"]);
    }

    [Fact]
    public void Un_telephone_questionne_sans_compte_est_declare_sans_jeu()
    {
        var read = GamePresenceReading.After(Nothing(), [Phone("tel")], []);

        Assert.Equal(GamePresence.Absent, read["tel"]);
    }

    [Fact]
    public void Un_telephone_questionne_avec_un_compte_est_declare_avec_le_jeu()
    {
        var read = GamePresenceReading.After(Nothing(), [Phone("tel")], [Account("tel")]);

        Assert.Equal(GamePresence.Present, read["tel"]);
    }

    [Fact]
    public void Un_telephone_hors_ligne_n_est_pas_questionne_donc_rien_n_est_conclu()
    {
        // The account search only asks connected devices, so an offline
        // one was never asked. Its absence from the answer proves
        // nothing.
        var read = GamePresenceReading.After(Nothing(), [Phone("tel", connected: false)], []);

        Assert.Equal(GamePresence.Unknown, read["tel"]);
    }

    [Fact]
    public void Un_telephone_hors_ligne_garde_le_verdict_rendu_quand_il_repondait()
    {
        var known = new Dictionary<string, GamePresence>(StringComparer.Ordinal)
        {
            ["tel"] = GamePresence.Absent,
        };

        var read = GamePresenceReading.After(known, [Phone("tel", connected: false)], []);

        Assert.Equal(GamePresence.Absent, read["tel"]);
    }

    [Fact]
    public void Un_telephone_disparu_ne_laisse_pas_de_verdict()
    {
        // The pruning the old set of searched devices never did: a
        // verdict outlives nothing.
        var known = new Dictionary<string, GamePresence>(StringComparer.Ordinal)
        {
            ["parti"] = GamePresence.Absent,
        };

        var read = GamePresenceReading.After(known, [Phone("tel")], [Account("tel")]);

        Assert.False(read.ContainsKey("parti"));
        Assert.Single(read);
    }

    [Fact]
    public void Un_compte_dont_le_telephone_n_est_pas_la_ne_rend_aucun_verdict()
    {
        // Remembered accounts of an absent phone come back in the list.
        // They must not conjure a device row that discovery did not see.
        var read = GamePresenceReading.After(Nothing(), [Phone("tel")], [Account("autre")]);

        Assert.Equal(GamePresence.Absent, read["tel"]);
        Assert.False(read.ContainsKey("autre"));
    }

    [Fact]
    public void Un_telephone_sans_jeu_qui_en_recoit_un_change_de_verdict()
    {
        var known = new Dictionary<string, GamePresence>(StringComparer.Ordinal)
        {
            ["tel"] = GamePresence.Absent,
        };

        var read = GamePresenceReading.After(known, [Phone("tel")], [Account("tel")]);

        Assert.Equal(GamePresence.Present, read["tel"]);
    }

    [Fact]
    public void Deux_telephones_sont_juges_chacun_pour_soi()
    {
        var read = GamePresenceReading.After(
            Nothing(),
            [Phone("avec"), Phone("sans")],
            [Account("avec")]);

        Assert.Equal(GamePresence.Present, read["avec"]);
        Assert.Equal(GamePresence.Absent, read["sans"]);
    }
}
