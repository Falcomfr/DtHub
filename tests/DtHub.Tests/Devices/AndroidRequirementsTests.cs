using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class AndroidRequirementsTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(34)]
    [InlineData(36)]
    public void Un_appareil_assez_recent_n_est_pas_refuse(int sdk)
    {
        Assert.Null(AndroidRequirements.DescribeVirtualDisplayShortfall(sdk, "16"));
    }

    [Fact]
    public void Un_appareil_trop_ancien_est_refuse_en_disant_sa_version()
    {
        var message = AndroidRequirements.DescribeVirtualDisplayShortfall(29, "10");

        Assert.NotNull(message);
        Assert.Contains("Android 10", message, StringComparison.Ordinal);
        Assert.Contains("Android 11", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sans_nom_de_version_le_niveau_d_api_est_dit_a_la_place()
    {
        var message = AndroidRequirements.DescribeVirtualDisplayShortfall(28, null);

        Assert.NotNull(message);
        Assert.Contains("28", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_niveau_d_api_inconnu_ne_fait_pas_refuser_l_appareil()
    {
        // Toutes les surcouches ne répondent pas à la lecture des propriétés.
        // Refuser sur une ignorance écarterait des appareils parfaitement
        // capables ; on les laisse essayer.
        Assert.Null(AndroidRequirements.DescribeVirtualDisplayShortfall(null, null));
    }
}
