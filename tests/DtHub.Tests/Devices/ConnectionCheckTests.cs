using DtHub.Core.Adb;
using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// The verdict rendered on the connection with the phone.
///
/// It extends one floor below what ADB can say: a cable that does not
/// carry data produces no line at all in "adb devices", and the
/// application then had nothing to answer with, even though Windows
/// knew everything.
/// </summary>
public class ConnectionCheckTests
{
    private static UsbFault Fault(int code) => new(UsbFault.KindOf(code), code, @"USB\VID_0000&PID_0002");

    [Fact]
    public void Un_telephone_qui_repond_l_emporte_sur_tout_le_reste()
    {
        var verdict = ConnectionCheck.Of(
            [AdbDeviceState.Unauthorized, AdbDeviceState.Device],
            [Fault(43)],
            toolsReady: true);

        Assert.Equal(ConnectionVerdict.Ready, verdict);
    }

    [Fact]
    public void Sans_outils_rien_d_autre_n_est_examine()
    {
        Assert.Equal(
            ConnectionVerdict.ToolsMissing,
            ConnectionCheck.Of([AdbDeviceState.Device], [], toolsReady: false));
    }

    [Theory]
    [InlineData(AdbDeviceState.Unauthorized)]
    [InlineData(AdbDeviceState.Authorizing)]
    public void Un_telephone_vu_mais_non_autorise_est_nomme_comme_tel(AdbDeviceState state)
    {
        Assert.Equal(
            ConnectionVerdict.WaitingAuthorization,
            ConnectionCheck.Of([state], [], toolsReady: true));
    }

    [Fact]
    public void Un_pilote_qui_refuse_l_acces_est_distingue_d_un_pilote_absent()
    {
        Assert.Equal(
            ConnectionVerdict.DriverRefused,
            ConnectionCheck.Of([AdbDeviceState.NoPermissions], [], toolsReady: true));
    }

    [Theory]
    [InlineData(43, ConnectionVerdict.UsbUnreadable)]
    [InlineData(28, ConnectionVerdict.UsbDriverMissing)]
    [InlineData(10, ConnectionVerdict.UsbOther)]
    public void Quand_adb_ne_voit_rien_c_est_windows_qui_parle(int code, ConnectionVerdict attendu)
    {
        Assert.Equal(attendu, ConnectionCheck.Of([], [Fault(code)], toolsReady: true));
    }

    [Fact]
    public void Le_defaut_le_plus_parlant_l_emporte()
    {
        // An ordinary machine often carries a faulty device that has
        // nothing to do with us: the most explicit one is the one
        // kept.
        Assert.Equal(
            ConnectionVerdict.UsbUnreadable,
            ConnectionCheck.Of([], [Fault(10), Fault(28), Fault(43)], toolsReady: true));
    }

    [Fact]
    public void Un_appareil_vu_par_adb_l_emporte_sur_un_defaut_d_un_autre_port()
    {
        // The fault can then only concern another port, and naming it
        // would send the user looking for a cable while the phone is
        // waiting for authorization.
        Assert.Equal(
            ConnectionVerdict.WaitingAuthorization,
            ConnectionCheck.Of([AdbDeviceState.Unauthorized], [Fault(43)], toolsReady: true));
    }

    [Fact]
    public void Rien_de_branche_et_rien_en_defaut_ne_dit_rien_de_plus()
    {
        Assert.Equal(ConnectionVerdict.NoDevice, ConnectionCheck.Of([], [], toolsReady: true));
    }

    [Fact]
    public void Chaque_verdict_porte_une_phrase()
    {
        foreach (var verdict in Enum.GetValues<ConnectionVerdict>())
        {
            var phrase = ConnectionCheck.Describe(verdict);

            Assert.False(string.IsNullOrWhiteSpace(phrase), verdict.ToString());

            // Strings.Get returns the key when the text is missing: a
            // key has neither space nor punctuation, a sentence does.
            Assert.Contains(" ", phrase, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void La_fiche_du_cable_ne_parait_que_lorsqu_elle_sert()
    {
        Assert.False(ConnectionCheck.NeedsCableHelp(ConnectionVerdict.Ready));
        Assert.False(ConnectionCheck.NeedsCableHelp(ConnectionVerdict.WaitingAuthorization));
        Assert.True(ConnectionCheck.NeedsCableHelp(ConnectionVerdict.UsbUnreadable));
        Assert.True(ConnectionCheck.NeedsCableHelp(ConnectionVerdict.NoDevice));
    }

    [Theory]
    [InlineData(0, UsbFaultKind.None)]
    [InlineData(43, UsbFaultKind.Unreadable)]
    [InlineData(28, UsbFaultKind.DriverMissing)]
    [InlineData(31, UsbFaultKind.Other)]
    public void Un_code_de_probleme_se_range_dans_sa_famille(int code, UsbFaultKind attendu) =>
        Assert.Equal(attendu, UsbFault.KindOf(code));

    [Fact]
    public void Un_telephone_qui_repond_n_a_rien_a_expliquer()
    {
        Assert.False(ConnectionCheck.NeedsExplaining(ConnectionVerdict.Ready));
    }

    [Fact]
    public void N_avoir_aucun_appareil_n_est_pas_une_panne()
    {
        // This is the application's resting state, the one you find
        // when opening it with nothing plugged in. Reporting it in a
        // warning block would make an absence look like a problem,
        // and the account list already says so just below.
        Assert.False(ConnectionCheck.NeedsExplaining(ConnectionVerdict.NoDevice));
    }

    [Theory]
    [InlineData(ConnectionVerdict.ToolsMissing)]
    [InlineData(ConnectionVerdict.WaitingAuthorization)]
    [InlineData(ConnectionVerdict.DriverRefused)]
    [InlineData(ConnectionVerdict.UsbUnreadable)]
    [InlineData(ConnectionVerdict.UsbDriverMissing)]
    [InlineData(ConnectionVerdict.UsbOther)]
    public void Ce_qui_est_la_et_ne_marche_pas_doit_etre_dit(ConnectionVerdict verdict)
    {
        // Nobody guesses that a driver is refusing the device or that
        // the PC must be authorized on the phone. This block exists
        // for these cases, and for these alone.
        Assert.True(ConnectionCheck.NeedsExplaining(verdict));
    }
}
