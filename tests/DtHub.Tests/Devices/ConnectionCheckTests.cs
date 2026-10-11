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

    private const string AdbClass = @"USB\Class_FF&SubClass_42&Prot_01";

    private static UsbPresence Usb(string id, params string[] compatible) => new(id, compatible, Service: null);

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

    [Theory]
    [InlineData(@"USB\VID_339B&PID_107E\ABC123", ConnectionVerdict.HonorNotDebugging)]
    [InlineData(@"usb\vid_339b&pid_5000\x", ConnectionVerdict.HonorNotDebugging)]
    [InlineData(@"USB\VID_12D1&PID_107E\ABC123", ConnectionVerdict.HuaweiNotDebugging)]
    public void Un_honor_que_windows_voit_sans_adb_est_nomme(string id, ConnectionVerdict attendu)
    {
        // Charge only by default, and a debugging option of their own:
        // ADB sees nothing, Windows sees the phone by its maker's code.
        Assert.Equal(attendu, ConnectionCheck.Of([], [], toolsReady: true, present: [Usb(id)]));
    }

    [Fact]
    public void Un_autre_appareil_usb_ne_change_rien()
    {
        Assert.Equal(
            ConnectionVerdict.NoDevice,
            ConnectionCheck.Of([], [], toolsReady: true, present: [Usb(@"USB\VID_046D&PID_C52B\5&1")]));
    }

    [Fact]
    public void Un_honor_vu_par_adb_n_a_rien_a_expliquer()
    {
        Assert.Equal(
            ConnectionVerdict.Ready,
            ConnectionCheck.Of([AdbDeviceState.Device], [], toolsReady: true, present: [Usb(@"USB\VID_339B&PID_107E\A")]));
    }

    [Fact]
    public void Un_defaut_usb_l_emporte_sur_la_marque()
    {
        // The fault names the cable or the driver, which comes first.
        Assert.Equal(
            ConnectionVerdict.UsbUnreadable,
            ConnectionCheck.Of([], [Fault(43)], toolsReady: true, present: [Usb(@"USB\VID_339B&PID_107E\A")]));
    }

    [Fact]
    public void Le_message_honor_ne_suppose_pas_le_transfert_de_fichiers()
    {
        // Some Honor phones offer photos but not files: either does.
        var phrase = ConnectionCheck.Describe(ConnectionVerdict.HonorNotDebugging);

        Assert.Contains("Honor", phrase, StringComparison.Ordinal);
        Assert.Contains("photos", phrase, StringComparison.Ordinal);
        Assert.Contains("Débogage ADB en mode Charger uniquement", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_debogage_tenu_par_un_pilote_qu_adb_ne_voit_pas_est_nomme()
    {
        // The interface is there, healthy for Windows, and no driver gave it
        // the Android interface adb looks for: Windows reports no fault.
        Assert.Equal(
            ConnectionVerdict.AdbDriverUnusable,
            ConnectionCheck.Of([], [], toolsReady: true, present: [Usb(@"USB\VID_339B&PID_107E&MI_01\7&1", AdbClass)], adbInterfaces: 0));
    }

    [Fact]
    public void Un_debogage_que_le_pilote_rend_a_adb_ne_change_rien()
    {
        Assert.Equal(
            ConnectionVerdict.NoDevice,
            ConnectionCheck.Of([], [], toolsReady: true, present: [Usb(@"USB\VID_2717&PID_FF48&MI_01\7&1", AdbClass)], adbInterfaces: 1));
    }

    [Fact]
    public void Le_pilote_inutilisable_passe_avant_la_marque()
    {
        // The Honor's steps say to turn debugging on: it already is.
        Assert.Equal(
            ConnectionVerdict.AdbDriverUnusable,
            ConnectionCheck.Of(
                [],
                [],
                toolsReady: true,
                present: [Usb(@"USB\VID_339B&PID_107E\A"), Usb(@"USB\VID_339B&PID_107E&MI_01\7&1", AdbClass)],
                adbInterfaces: 0));
    }

    [Fact]
    public void Le_resume_usb_ne_garde_que_les_telephones_et_jamais_leur_numero()
    {
        var summary = UsbPresence.Summarize(
        [
            new(@"USB\VID_339B&PID_107E\AB12CD34EF", [@"USB\Class_00"], "WUDFWpdMtp"),
            new(@"USB\VID_339B&PID_107E&MI_01\7&2A3B&0&0001", [AdbClass], "hwusbdev"),
            new(@"USB\VID_046D&PID_C52B\5&1", [@"USB\Class_03"], "HidUsb"),
        ]);

        Assert.Contains(@"VID_339B&PID_107E (WUDFWpdMtp)", summary, StringComparison.Ordinal);
        Assert.Contains(@"VID_339B&PID_107E&MI_01 débogage (hwusbdev)", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("AB12CD34EF", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("VID_046D", summary, StringComparison.Ordinal);
    }
}
