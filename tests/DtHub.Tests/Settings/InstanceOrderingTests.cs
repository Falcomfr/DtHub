using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

/// <summary>
/// L'ordre voulu par l'utilisateur, tel qu'il est tenu dans les réglages.
/// Ces fonctions sont pures : aucun disque, aucun téléphone.
/// </summary>
public sealed class InstanceOrderingTests
{
    private static StoredInstance Entry(string deviceId, int userId, int order) => new()
    {
        DeviceId = deviceId,
        UserId = userId,
        PackageName = "com.ankama.dofustouch",
        UserName = userId == 0 ? "Principal" : "XSpace",
        Order = order,
    };

    /// <summary>Deux téléphones, deux profils chacun, rangs volontairement creux.</summary>
    private static AppSettingsDocument TwoDevices() => new()
    {
        Instances =
        [
            Entry("PHONE-A", 0, 0),
            Entry("PHONE-A", 999, 7),
            Entry("PHONE-B", 0, 12),
            Entry("PHONE-B", 999, 40),
        ],
    };

    private static IReadOnlyList<string> Keys(AppSettingsDocument settings) =>
        [.. settings.Instances.OrderBy(i => i.Order).Select(i => $"{i.DeviceId}/{i.UserId}")];

    [Fact]
    public void Les_rangs_sont_resserres_apres_normalisation()
    {
        var settings = TwoDevices();

        InstanceOrdering.Normalize(settings);

        Assert.Equal([0, 1, 2, 3], [.. settings.Instances.Select(i => i.Order).Order()]);
    }

    [Fact]
    public void Deux_instances_ne_partagent_jamais_un_rang()
    {
        var settings = TwoDevices();
        settings.Instances[2].Order = 7;

        InstanceOrdering.Normalize(settings);

        Assert.Equal(
            settings.Instances.Count,
            settings.Instances.Select(i => i.Order).Distinct().Count());
    }

    [Fact]
    public void Les_instances_restent_groupees_par_appareil()
    {
        var settings = TwoDevices();

        InstanceOrdering.Normalize(settings);

        Assert.Equal(["PHONE-A/0", "PHONE-A/999", "PHONE-B/0", "PHONE-B/999"], Keys(settings));
    }

    [Fact]
    public void L_ordre_des_appareils_est_deduit_quand_il_n_a_jamais_ete_enregistre()
    {
        var settings = TwoDevices();

        InstanceOrdering.Normalize(settings);

        Assert.Equal(["PHONE-A", "PHONE-B"], settings.DeviceOrder);
    }

    [Fact]
    public void Descendre_une_instance_l_echange_avec_la_suivante_du_meme_appareil()
    {
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        Assert.True(InstanceOrdering.MoveInstance(settings, "PHONE-A|0|com.ankama.dofustouch", 1));

        Assert.Equal(["PHONE-A/999", "PHONE-A/0", "PHONE-B/0", "PHONE-B/999"], Keys(settings));
    }

    [Fact]
    public void Une_instance_ne_franchit_jamais_la_frontiere_de_son_appareil()
    {
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        // La dernière du premier téléphone : la descendre la ferait entrer
        // dans le second, ce qui n'aurait pas de sens à l'écran.
        Assert.False(InstanceOrdering.MoveInstance(settings, "PHONE-A|999|com.ankama.dofustouch", 1));
        Assert.Equal(["PHONE-A/0", "PHONE-A/999", "PHONE-B/0", "PHONE-B/999"], Keys(settings));
    }

    [Fact]
    public void Monter_un_appareil_deplace_toutes_ses_instances_en_bloc()
    {
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        Assert.True(InstanceOrdering.MoveDevice(settings, "PHONE-B", -1));

        Assert.Equal(["PHONE-B/0", "PHONE-B/999", "PHONE-A/0", "PHONE-A/999"], Keys(settings));
        Assert.Equal(["PHONE-B", "PHONE-A"], settings.DeviceOrder);
    }

    [Fact]
    public void Un_appareil_deja_en_tete_ne_monte_pas()
    {
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        Assert.False(InstanceOrdering.MoveDevice(settings, "PHONE-A", -1));
    }

    [Fact]
    public void Un_appareil_absent_de_l_ordre_memorise_passe_en_dernier()
    {
        var settings = TwoDevices();
        settings.DeviceOrder = ["PHONE-B"];

        InstanceOrdering.Normalize(settings);

        Assert.Equal(["PHONE-B", "PHONE-A"], settings.DeviceOrder);
    }

    [Fact]
    public void Un_appareil_sans_instance_disparait_de_l_ordre()
    {
        var settings = TwoDevices();
        settings.DeviceOrder = ["PHONE-A", "PHONE-DISPARU", "PHONE-B"];

        InstanceOrdering.Normalize(settings);

        Assert.Equal(["PHONE-A", "PHONE-B"], settings.DeviceOrder);
    }

    [Fact]
    public void Une_cle_oubliee_dans_un_reordonnancement_ne_perd_pas_son_instance()
    {
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        InstanceOrdering.ReorderInstances(settings, "PHONE-A", ["PHONE-A|999|com.ankama.dofustouch"]);

        Assert.Equal(["PHONE-A/999", "PHONE-A/0", "PHONE-B/0", "PHONE-B/999"], Keys(settings));
    }
}
