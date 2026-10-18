using DtHub.Core.Adb;
using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class CableArrivalTests
{
    private static AndroidDevice Phone(string serial, AdbConnectionKind kind, AdbDeviceState state) =>
        new() { Id = serial, Serial = serial, ConnectionKind = kind, State = state };

    [Fact]
    public void Un_telephone_branche_apres_l_ouverture_est_celui_qu_on_attendait()
    {
        HashSet<string> before = ["USB_DEJA_LA"];

        var arrived = CableArrival.Arrived(before,
        [
            Phone("USB_DEJA_LA", AdbConnectionKind.Usb, AdbDeviceState.Device),
            Phone("USB_NOUVEAU", AdbConnectionKind.Usb, AdbDeviceState.Device),
        ]);

        Assert.Equal("USB_NOUVEAU", arrived?.Serial);
    }

    [Fact]
    public void Ni_un_telephone_deja_branche_ni_un_telephone_sans_fil_ne_comptent()
    {
        // The window waits for the cable the user is about to plug in. A
        // phone already there, or one that just reconnected over Wi-Fi,
        // would close it on a phone nobody plugged.
        HashSet<string> before = ["USB_DEJA_LA"];

        Assert.Null(CableArrival.Arrived(before,
        [
            Phone("USB_DEJA_LA", AdbConnectionKind.Usb, AdbDeviceState.Device),
            Phone("192.168.1.16:41234", AdbConnectionKind.Wireless, AdbDeviceState.Device),
        ]));
    }

    [Theory]
    [InlineData(AdbDeviceState.Unauthorized)]
    [InlineData(AdbDeviceState.Authorizing)]
    public void Un_telephone_branche_qui_n_a_pas_accepte_attend_la_demande(AdbDeviceState state)
    {
        AndroidDevice[] now = [Phone("USB_NOUVEAU", AdbConnectionKind.Usb, state)];

        Assert.Null(CableArrival.Arrived(new HashSet<string>(), now));
        Assert.True(CableArrival.AwaitsApproval(now));
    }

    [Fact]
    public void Sans_telephone_en_attente_rien_n_est_a_accepter()
    {
        Assert.False(CableArrival.AwaitsApproval(
        [
            Phone("USB_DEJA_LA", AdbConnectionKind.Usb, AdbDeviceState.Device),
            Phone("192.168.1.16:41234", AdbConnectionKind.Wireless, AdbDeviceState.Unauthorized),
        ]));
    }

    [Fact]
    public void Un_telephone_debranche_puis_rebranche_compte_comme_arrive()
    {
        // Plugged and accepted before the window opened, then unplugged and
        // plugged back: that is the arrival a player makes to be seen.
        HashSet<string> before = ["USB_DEJA_LA"];

        CableArrival.ForgetUnplugged(before, []);

        Assert.Equal("USB_DEJA_LA", CableArrival.Arrived(before,
            [Phone("USB_DEJA_LA", AdbConnectionKind.Usb, AdbDeviceState.Device)])?.Serial);
    }

    [Theory]
    [InlineData(AdbDeviceState.Device)]
    [InlineData(AdbDeviceState.Unauthorized)]
    public void Un_telephone_toujours_branche_reste_connu(AdbDeviceState state)
    {
        HashSet<string> before = ["USB_DEJA_LA"];

        CableArrival.ForgetUnplugged(before, [Phone("USB_DEJA_LA", AdbConnectionKind.Usb, state)]);

        Assert.Contains("USB_DEJA_LA", before);
    }

    [Fact]
    public void Un_telephone_retenu_hors_ligne_n_est_plus_branche()
    {
        // Discovery also returns the remembered phones, unplugged ones
        // included, as offline.
        HashSet<string> before = ["USB_DEJA_LA"];

        CableArrival.ForgetUnplugged(before, [Phone("USB_DEJA_LA", AdbConnectionKind.Usb, AdbDeviceState.Offline)]);

        Assert.Empty(before);
    }
}
