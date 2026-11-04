using System.Globalization;
using System.Text.Json;

using DtHub.Core.Dependencies;
using DtHub.Core.IPhone;
using DtHub.Core.Localization;
using DtHub.Core.Processes;
using DtHub.Core.Storage;
using DtHub.Infrastructure.Dependencies;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.IPhone;

/// <summary>
/// pymobiledevice3, run through uv. It lists the iPhones, and serves one's
/// screen as a web page whose clicks become touches (<c>serve-web</c>,
/// iOS 27 and later), over a tunnel that needs no administrator rights.
/// uv brings its own Python: the player installs nothing but Apple's
/// "Apple Devices" app, which carries the USB service.
/// </summary>
public sealed partial class PyMobileDevice
{
    /// <summary>Pinned: the error texts <see cref="Explain"/> reads are this version's.</summary>
    public const string Version = "11.49.5";

    // The first call downloads Python and about ninety packages, near
    // three hundred megabytes; fifteen seconds on a fast line.
    private static readonly TimeSpan FirstRunTimeout = TimeSpan.FromMinutes(10);

    private readonly IDependencyProvisioner _provisioner;
    private readonly IProcessRunner _runner;
    private readonly IProcessLauncher _launcher;
    private readonly IAppPaths _paths;
    private readonly ILogger<PyMobileDevice> _logger;

    public PyMobileDevice(
        IDependencyProvisioner provisioner,
        IProcessRunner runner,
        IProcessLauncher launcher,
        IAppPaths paths,
        ILogger<PyMobileDevice> logger)
    {
        _provisioner = provisioner;
        _runner = runner;
        _launcher = launcher;
        _paths = paths;
        _logger = logger;
    }

    private string Root => Path.Combine(_paths.ToolsDirectory, "iphone");

    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task<IReadOnlyList<IPhoneDevice>> ListAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(udid: null, cancellationToken, "usbmux", "list").ConfigureAwait(false);

        try
        {
            return IPhoneDevice.ParseList(result.StandardOutput);
        }
        catch (JsonException exception)
        {
            throw new IPhoneException(Explain(result.StandardError), exception);
        }
    }

    /// <summary>
    /// Serves the device's screen on <c>http://127.0.0.1:port/</c> until
    /// the session is killed, which ends uv's Python with it.
    /// </summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task<IProcessSession> StartMirrorAsync(
        IPhoneDevice device, int port, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Xcode does this on its own: the display service comes with the
        // developer disk image. Does nothing when it is already there.
        await RunAsync(device.Udid, cancellationToken, "mounter", "auto-mount").ConfigureAwait(false);

        var uv = await UvAsync(cancellationToken).ConfigureAwait(false);
        var request = Request(
            uv, Root, device.Udid,
            "developer", "core-device", "display", "serve-web",
            "--http-port", port.ToString(CultureInfo.InvariantCulture)) with
        {
            KillEntireTree = true,
        };

        LogStarting(device.Name, port);

        try
        {
            return _launcher.Start(request);
        }
        catch (ProcessLaunchException exception)
        {
            throw new IPhoneException(exception.Message, exception);
        }
    }

    /// <summary>The uv command line for a pymobiledevice3 command.</summary>
    public static ProcessRequest Request(string uvPath, string root, string? udid, params string[] arguments)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Only uv's own Python: one the player installed would make
            // every machine behave differently.
            ["UV_PYTHON_PREFERENCE"] = "only-managed",
            ["UV_CACHE_DIR"] = Path.Combine(root, "cache"),
            ["UV_PYTHON_INSTALL_DIR"] = Path.Combine(root, "python"),
            ["UV_TOOL_DIR"] = Path.Combine(root, "tools"),
            ["NO_COLOR"] = "1",
        };

        if (udid is not null)
        {
            environment["PYMOBILEDEVICE3_UDID"] = udid;
        }

        return new ProcessRequest
        {
            FileName = uvPath,
            Arguments =
            [
                "tool", "run", "--python", "3.13", "--from", $"pymobiledevice3=={Version}",
                "pymobiledevice3", .. arguments,
            ],
            Environment = environment,
        };
    }

    /// <summary>
    /// What to tell the player, from the tool's error output. Its messages
    /// are its exceptions' docstrings.
    /// </summary>
    public static string Explain(string error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Contains("usbmuxd", StringComparison.Ordinal))
        {
            return Strings.Get("IPhoneAppleDevicesMissing");
        }

        if (error.Contains("Developer Mode", StringComparison.Ordinal))
        {
            return Strings.Get("IPhoneDeveloperModeOff");
        }

        if (error.Contains("password protected", StringComparison.Ordinal)
            || error.Contains("paired", StringComparison.OrdinalIgnoreCase)
            || error.Contains("pairing", StringComparison.OrdinalIgnoreCase))
        {
            return Strings.Get("IPhoneTrust");
        }

        if (error.Contains("not connected", StringComparison.Ordinal))
        {
            return Strings.Get("IPhoneUnplugged");
        }

        if (error.Contains("Screen streaming needs iOS", StringComparison.Ordinal))
        {
            return Strings.Get("IPhoneNeedsIos27");
        }

        var line = error.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Contains("ERROR ", StringComparison.Ordinal));

        return line is null
            ? Strings.Get("IPhoneFailed")
            : line[(line.IndexOf("ERROR ", StringComparison.Ordinal) + "ERROR ".Length)..];
    }

    private async Task<ProcessResult> RunAsync(string? udid, CancellationToken cancellationToken, params string[] arguments)
    {
        var uv = await UvAsync(cancellationToken).ConfigureAwait(false);
        var request = Request(uv, Root, udid, arguments) with { Timeout = FirstRunTimeout };

        var result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            LogFailed(request.ToDisplayString(), result.StandardError);
            throw new IPhoneException(Explain(result.StandardError));
        }

        return result;
    }

    private async Task<string> UvAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _provisioner
                .EnsureAvailableAsync(DependencyManifest.Get(DependencyManifest.UvKey), progress: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DependencyProvisioningException exception)
        {
            throw new IPhoneException(exception.Message, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Affichage de l'iPhone {name} sur le port {port}.")]
    private partial void LogStarting(string name, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{command} a échoué : {error}")]
    private partial void LogFailed(string command, string error);
}
