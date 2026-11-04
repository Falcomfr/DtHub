using DtHub.Core.Localization;
using DtHub.Infrastructure.IPhone;

namespace DtHub.Tests.IPhone;

/// <summary>
/// How DT Hub calls pymobiledevice3 through uv, and what it tells the
/// player when that fails. The error lines are the ones the tool printed
/// on 2026-11-04.
/// </summary>
public class PyMobileDeviceTests
{
    private static readonly string Root = Path.Combine("C:", "outils", "iphone");

    [Fact]
    public void La_commande_passe_par_uv_avec_une_version_figee()
    {
        var request = PyMobileDevice.Request("uv.exe", Root, udid: null, "usbmux", "list");

        Assert.Equal("uv.exe", request.FileName);
        Assert.Equal(
            ["tool", "run", "--python", "3.13", "--from", $"pymobiledevice3=={PyMobileDevice.Version}",
             "pymobiledevice3", "usbmux", "list"],
            request.Arguments);
    }

    [Fact]
    public void Python_et_les_paquets_restent_dans_le_dossier_de_DT_Hub()
    {
        var request = PyMobileDevice.Request("uv.exe", Root, udid: null, "usbmux", "list");

        // Only uv's own Python: one the player installed would make every
        // machine behave differently.
        Assert.Equal("only-managed", request.Environment["UV_PYTHON_PREFERENCE"]);
        Assert.Equal(Path.Combine(Root, "cache"), request.Environment["UV_CACHE_DIR"]);
        Assert.Equal(Path.Combine(Root, "python"), request.Environment["UV_PYTHON_INSTALL_DIR"]);
        Assert.Equal(Path.Combine(Root, "tools"), request.Environment["UV_TOOL_DIR"]);
        Assert.False(request.Environment.ContainsKey("PYMOBILEDEVICE3_UDID"));
    }

    [Fact]
    public void L_iPhone_vise_passe_par_l_environnement()
    {
        // The tool reads --udid itself from its command line for the tunnel,
        // the variable reaches every subcommand the same way.
        var request = PyMobileDevice.Request("uv.exe", Root, "00008020-000A", "mounter", "auto-mount");

        Assert.Equal("00008020-000A", request.Environment["PYMOBILEDEVICE3_UDID"]);
    }

    [Fact]
    public void Un_iOS_trop_ancien_est_explique()
    {
        const string error = """
             Downloaded pymobiledevice3
            Installed 90 packages in 1.86s
            2026-11-04 14:09:12 Falcom pymobiledevice3.__main__[17364] ERROR Screen streaming needs iOS 27.0 or later; this device runs iOS 18.7.9
            2026-11-04 14:09:12 Falcom pymobiledevice3.__main__[17364] INFO Run again with -v for the full traceback (include it when reporting an issue)
            """;

        Assert.Contains("iOS 27", PyMobileDevice.Explain(error), StringComparison.Ordinal);
    }

    [Fact]
    public void Le_service_usb_d_Apple_absent_renvoie_vers_l_appli_Appareils_Apple()
    {
        const string error = "ERROR Failed to connect to usbmuxd socket. Make sure it's running.";

        Assert.Equal(Strings.Get("IPhoneAppleDevicesMissing"), PyMobileDevice.Explain(error));
    }

    [Theory]
    // The tool's messages are its exceptions' docstrings, as of 11.49.5.
    [InlineData("ERROR Developer Mode is disabled. You can try to enable it using: python3 -m pymobiledevice3 amfi enable-developer-mode", "IPhoneDeveloperModeOff")]
    [InlineData("ERROR Device is password protected. Please unlock and retry.", "IPhoneTrust")]
    [InlineData("ERROR Device is not paired.", "IPhoneTrust")]
    [InlineData("ERROR The device no longer accepts this computer's pairing record. Pair it again.", "IPhoneTrust")]
    [InlineData("ERROR Device is not connected.", "IPhoneUnplugged")]
    public void Les_refus_de_l_iPhone_disent_quoi_faire(string error, string key)
    {
        Assert.Equal(Strings.Get(key), PyMobileDevice.Explain(error));
    }

    [Fact]
    public void Une_erreur_inconnue_garde_le_texte_de_l_outil()
    {
        const string error = "2026-11-04 14:09:12 Falcom pymobiledevice3.__main__[1] ERROR Something odd happened";

        Assert.Equal("Something odd happened", PyMobileDevice.Explain(error));
    }

    [Fact]
    public void Une_sortie_vide_donne_un_message_general()
    {
        Assert.Equal(Strings.Get("IPhoneFailed"), PyMobileDevice.Explain(string.Empty));
    }
}
