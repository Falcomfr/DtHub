using DtHub.Core.Dependencies;
using DtHub.Core.IPhone;
using DtHub.Core.Localization;
using DtHub.Infrastructure.IPhone;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.IPhone;

/// <summary>
/// How DT Hub sets up and calls pymobiledevice3, and what it tells the
/// player when that fails. The error lines are the ones the tool printed
/// on 2026-11-04.
/// </summary>
public class PyMobileDeviceTests
{
    private static readonly string Root = Path.Combine("C:", "outils", "iphone");

    [Fact]
    public void Uv_n_utilise_que_son_Python_et_reste_dans_le_dossier_de_DT_Hub()
    {
        var request = PyMobileDevice.UvRequest("uv.exe", Root, "venv");

        // One the player installed would make every machine behave differently.
        Assert.Equal("only-managed", request.Environment["UV_PYTHON_PREFERENCE"]);
        Assert.Equal(Path.Combine(Root, "cache"), request.Environment["UV_CACHE_DIR"]);
        Assert.Equal(Path.Combine(Root, "python"), request.Environment["UV_PYTHON_INSTALL_DIR"]);
        Assert.True(request.KillEntireTree);
    }

    [Fact]
    public void L_outil_tourne_dans_le_Python_installe_et_s_arrete_avec_ses_enfants()
    {
        var request = PyMobileDevice.ToolRequest("python.exe", udid: null, "usbmux", "list", "--usb");

        Assert.Equal("python.exe", request.FileName);
        Assert.Equal(["-m", "pymobiledevice3", "usbmux", "list", "--usb"], request.Arguments);
        Assert.False(request.Environment.ContainsKey("PYMOBILEDEVICE3_UDID"));
        Assert.True(request.KillEntireTree);
    }

    [Fact]
    public void L_iPhone_vise_passe_par_l_environnement()
    {
        var request = PyMobileDevice.ToolRequest("python.exe", "00008020-000A", "mounter", "auto-mount");

        Assert.Equal("00008020-000A", request.Environment["PYMOBILEDEVICE3_UDID"]);
    }

    [Fact]
    public void La_liste_figee_porte_pymobiledevice3_a_sa_version_et_des_empreintes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DtHub.slnx")))
        {
            directory = directory.Parent;
        }

        var lines = File.ReadAllLines(Path.Combine(directory!.FullName, "build", "iphone-requirements.txt"));

        Assert.Contains($"pymobiledevice3=={PyMobileDevice.Version} \\", lines);
        Assert.Contains(lines, l => l.TrimStart().StartsWith("--hash=sha256:", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_iOS_trop_ancien_est_explique()
    {
        const string error = """
             Downloaded pymobiledevice3
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
    public void Seule_la_derniere_erreur_compte()
    {
        const string error = """
            INFO Starting the RemotePairing tunnel over usbmuxd
            ERROR Device is not connected.
            """;

        Assert.Equal(Strings.Get("IPhoneUnplugged"), PyMobileDevice.Explain(error));
    }

    [Fact]
    public void Une_erreur_inconnue_garde_le_texte_de_l_outil()
    {
        const string error = "2026-11-04 14:09:12 Falcom pymobiledevice3.__main__[1] ERROR Something odd happened";

        Assert.Equal("Something odd happened", PyMobileDevice.Explain(error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("error: Failed to download distribution")]
    public void Sans_ligne_d_erreur_de_l_outil_le_message_est_general(string error)
    {
        Assert.Equal(Strings.Get("IPhoneFailed"), PyMobileDevice.Explain(error));
    }

    [Fact]
    public async Task Un_Python_abime_est_efface_pour_que_l_essai_suivant_le_retelecharge()
    {
        // Seen on 2026-11-09: an antivirus removed python.exe, and every try
        // failed the same way until the player deleted the folder by hand.
        var root = Directory.CreateTempSubdirectory("dthub-iphone-").FullName;
        var paths = new FakeAppPaths(root);
        var python = Path.Combine(paths.ToolsDirectory, "iphone", "python");
        Directory.CreateDirectory(Path.Combine(python, "cpython-3.13.18-windows-x86_64-none"));
        var runner = new FakeProcessRunner().Respond(
            "venv", standardError: "error: Python interpreter not found at `python.exe`", exitCode: 2);
        using var tools = new PyMobileDevice(
            new UvAlreadyThere(), runner, new FakeProcessLauncher(), paths, NullLogger<PyMobileDevice>.Instance);

        await Assert.ThrowsAsync<IPhoneException>(() => tools.ListAsync(CancellationToken.None));

        Assert.False(Directory.Exists(python));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void La_liste_ne_cherche_d_iPhone_qu_une_fois_les_outils_installes()
    {
        // A player who never opened the iPhone window must not start Python
        // at each sweep of the device list.
        var root = Directory.CreateTempSubdirectory("dthub-iphone-").FullName;
        var paths = new FakeAppPaths(root);
        using var tools = new PyMobileDevice(
            new UvAlreadyThere(), new FakeProcessRunner(), new FakeProcessLauncher(), paths, NullLogger<PyMobileDevice>.Instance);

        Assert.False(tools.IsInstalled);

        var scripts = Path.Combine(paths.ToolsDirectory, "iphone", "venv", OperatingSystem.IsWindows() ? "Scripts" : "bin");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, OperatingSystem.IsWindows() ? "python.exe" : "python"), string.Empty);

        Assert.True(tools.IsInstalled);
        Directory.Delete(root, recursive: true);
    }

    private sealed class UvAlreadyThere : IDependencyProvisioner
    {
        public Task<string> EnsureAvailableAsync(
            ExternalDependency dependency,
            IProgress<ProvisioningProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.FromResult("uv.exe");

        public string? TryGetExistingPath(ExternalDependency dependency) => "uv.exe";
    }
}
