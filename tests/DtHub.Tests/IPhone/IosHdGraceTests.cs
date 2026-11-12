using DtHub.Core.IPhone;

namespace DtHub.Tests.IPhone;

public class IosHdGraceTests
{
    private static readonly DateTime Start = new(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Un_appareil_absent_moins_de_15_s_apres_le_debut_du_HD_est_tolere()
    {
        Assert.True(IosHdGrace.Tolerates(Start, Start.AddSeconds(14.9)));
    }

    [Fact]
    public void A_15_s_l_absence_n_est_plus_toleree()
    {
        Assert.False(IosHdGrace.Tolerates(Start, Start.AddSeconds(15)));
    }

    [Fact]
    public void Une_horloge_qui_recule_reste_toleree()
    {
        Assert.True(IosHdGrace.Tolerates(Start, Start.AddSeconds(-1)));
    }
}
