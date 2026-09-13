using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

/// <summary>
/// The order the user wants, as held in the settings. It is global
/// and free-form: an instance can place itself between two instances
/// of another device. These functions are pure: no disk, no phone.
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

    /// <summary>
    /// Two phones, two profiles each, ranks deliberately sparse.
    /// </summary>
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

    private static string Key(string deviceId, int userId) =>
        $"{deviceId}|{userId}|com.ankama.dofustouch";

    private static IReadOnlyList<string> Names(AppSettingsDocument settings) =>
        [.. settings.Instances.OrderBy(i => i.Order).Select(i => $"{i.DeviceId}/{i.UserId}")];

    [Fact]
    public void Les_rangs_sont_resserres_apres_normalisation()
    {
        var settings = TwoDevices();

        InstanceOrdering.Normalize(settings);

        Assert.Equal([0, 1, 2, 3], [.. settings.Instances.Select(i => i.Order).Order()]);
    }

    [Fact]
    public void La_normalisation_conserve_l_ordre_courant()
    {
        var settings = TwoDevices();

        InstanceOrdering.Normalize(settings);

        Assert.Equal(
            ["PHONE-A/0", "PHONE-A/999", "PHONE-B/0", "PHONE-B/999"],
            Names(settings));
    }

    [Fact]
    public void Deux_instances_ne_partagent_jamais_un_rang()
    {
        // Ranks used to be assigned once and for all at discovery:
        // two instances sharing a rank left the order dependent on
        // insertion order.
        var settings = new AppSettingsDocument
        {
            Instances = [Entry("PHONE-A", 0, 3), Entry("PHONE-B", 0, 3)],
        };

        InstanceOrdering.Normalize(settings);

        Assert.Equal(["PHONE-A/0", "PHONE-B/0"], Names(settings));
        Assert.Equal([0, 1], [.. settings.Instances.Select(i => i.Order).Order()]);
    }

    [Fact]
    public void Une_instance_se_glisse_entre_celles_d_un_autre_appareil()
    {
        // This is the whole point of the change: the order no longer
        // knows about device boundaries.
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        var moved = InstanceOrdering.MoveInstance(
            settings, Key("PHONE-B", 0), Key("PHONE-A", 999), above: true);

        Assert.True(moved);
        Assert.Equal(
            ["PHONE-A/0", "PHONE-B/0", "PHONE-A/999", "PHONE-B/999"],
            Names(settings));
    }

    [Fact]
    public void Les_rangs_restent_denses_apres_un_deplacement()
    {
        var settings = TwoDevices();

        InstanceOrdering.MoveInstance(settings, Key("PHONE-B", 999), Key("PHONE-A", 0), above: true);

        Assert.Equal([0, 1, 2, 3], [.. settings.Instances.Select(i => i.Order).Order()]);
    }

    [Fact]
    public void Deposer_au_dessus_du_voisin_du_dessus_echange_les_deux()
    {
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        InstanceOrdering.MoveInstance(settings, Key("PHONE-A", 999), Key("PHONE-A", 0), above: true);

        Assert.Equal(
            ["PHONE-A/999", "PHONE-A/0", "PHONE-B/0", "PHONE-B/999"],
            Names(settings));
    }

    [Fact]
    public void Deposer_en_dessous_de_la_derniere_place_l_instance_en_fin()
    {
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        InstanceOrdering.MoveInstance(settings, Key("PHONE-A", 0), Key("PHONE-B", 999), above: false);

        Assert.Equal(
            ["PHONE-A/999", "PHONE-B/0", "PHONE-B/999", "PHONE-A/0"],
            Names(settings));
    }

    [Fact]
    public void Un_deplacement_sur_soi_meme_ne_change_rien()
    {
        var settings = TwoDevices();

        Assert.False(InstanceOrdering.MoveInstance(
            settings, Key("PHONE-A", 0), Key("PHONE-A", 0), above: true));
    }

    [Fact]
    public void Une_cle_inconnue_ne_deplace_rien()
    {
        var settings = TwoDevices();

        Assert.False(InstanceOrdering.MoveInstance(
            settings, Key("PHONE-Z", 0), Key("PHONE-A", 0), above: true));
    }

    [Fact]
    public void Un_deplacement_ne_derange_pas_les_instances_d_un_appareil_absent()
    {
        // The displayed list only shows reachable devices, while the
        // settings carry everything: a move counted in visible
        // positions would have moved the wrong instance.
        var settings = new AppSettingsDocument
        {
            Instances = [Entry("PHONE-A", 0, 0), Entry("ABSENT", 0, 1), Entry("PHONE-A", 999, 2)],
        };

        InstanceOrdering.MoveInstance(settings, Key("PHONE-A", 999), Key("PHONE-A", 0), above: true);

        Assert.Equal(["PHONE-A/999", "PHONE-A/0", "ABSENT/0"], Names(settings));
    }

    [Fact]
    public void Une_instance_neuve_se_place_a_la_suite_de_celles_de_son_appareil()
    {
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        InstanceOrdering.Add(settings, Entry("PHONE-A", 42, 0));

        Assert.Equal(
            ["PHONE-A/0", "PHONE-A/999", "PHONE-A/42", "PHONE-B/0", "PHONE-B/999"],
            Names(settings));
    }

    [Fact]
    public void Une_instance_neuve_d_un_appareil_inconnu_se_place_en_fin()
    {
        var settings = TwoDevices();
        InstanceOrdering.Normalize(settings);

        InstanceOrdering.Add(settings, Entry("PHONE-C", 0, 0));

        Assert.Equal("PHONE-C/0", Names(settings)[^1]);
    }

    [Fact]
    public void Une_cle_oubliee_dans_un_reordonnancement_ne_perd_pas_son_instance()
    {
        var settings = TwoDevices();

        InstanceOrdering.ReorderInstances(settings, [Key("PHONE-B", 999), Key("PHONE-A", 0)]);

        Assert.Equal(4, settings.Instances.Count);
        Assert.Equal(["PHONE-B/999", "PHONE-A/0"], [.. Names(settings).Take(2)]);
    }
}
