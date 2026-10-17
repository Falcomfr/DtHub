using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// A phone gone for weeks was still asked to reconnect every five seconds:
/// a Mi 9T Pro last seen on 2026-09-20 still held the panel's sweep up to
/// five seconds a pass on 2026-10-17.
/// </summary>
public class DeviceDormancyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Un_telephone_vu_dans_la_journee_n_est_pas_endormi()
    {
        Assert.False(DeviceDormancy.IsDormant(Now.AddHours(-23), Now));
    }

    [Fact]
    public void Un_telephone_absent_depuis_plus_d_un_jour_est_endormi()
    {
        Assert.True(DeviceDormancy.IsDormant(Now.AddDays(-27), Now));
    }

    [Fact]
    public void Un_telephone_jamais_vu_n_est_pas_endormi()
    {
        // Just paired, it has no sighting yet and must be tried at once.
        Assert.False(DeviceDormancy.IsDormant(null, Now));
    }
}
