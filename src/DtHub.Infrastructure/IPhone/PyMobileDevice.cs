using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
/// pymobiledevice3, for the iPhone window only (D227). It lists the iPhones,
/// and serves one's screen as a web page whose clicks become touches
/// (<c>serve-web</c>, iOS 27 and later), over a tunnel that needs no
/// administrator rights.
///
/// The player installs nothing but Apple's "Apple Devices" app, which
/// carries the USB service. uv, from the manifest, sets up its own Python
/// then installs <c>build/iphone-requirements.txt</c> with
/// <c>--require-hashes</c>: every package is checked against its hash.
/// </summary>
public sealed partial class PyMobileDevice : IDisposable
{
    /// <summary>Pinned: the error texts <see cref="Explain"/> reads are this version's.</summary>
    public const string Version = "11.49.5";

    /// <summary>The exact Python uv installs, from its own pinned downloads.</summary>
    public const string PythonVersion = "3.13.18";

    private const string RequirementsResource = "DtHub.Infrastructure.iphone-requirements.txt";

    // The first setup downloads Python and ninety packages, near three
    // hundred megabytes: fifteen seconds measured on fiber.
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

    // Mounting fetches the developer disk image and has Apple sign it.
    private static readonly TimeSpan MountTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(1);

    private readonly IDependencyProvisioner _provisioner;
    private readonly IProcessRunner _runner;
    private readonly IProcessLauncher _launcher;
    private readonly IAppPaths _paths;
    private readonly ILogger<PyMobileDevice> _logger;

    // One call at a time, across every window: two first setups would
    // download into the same folders.
    private readonly SemaphoreSlim _gate = new(1, 1);

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

    private string Venv => Path.Combine(Root, "venv");

    private string Python => OperatingSystem.IsWindows()
        ? Path.Combine(Venv, "Scripts", "python.exe")
        : Path.Combine(Venv, "bin", "python");

    /// <summary>
    /// Set up once already: the device list looks for iPhones only then, so
    /// a player who never opened the iPhone window starts no Python.
    /// </summary>
    public bool IsInstalled => File.Exists(Python);

    /// <summary>The iPhones plugged in over USB; Wi-Fi ones would show twice.</summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task<IReadOnlyList<IPhoneDevice>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var python = await EnsureInstalledAsync(cancellationToken).ConfigureAwait(false);
            var result = await RunAsync(
                ToolRequest(python, udid: null, "usbmux", "list", "--usb") with { Timeout = ListTimeout },
                cancellationToken).ConfigureAwait(false);

            try
            {
                return IPhoneDevice.ParseList(result.StandardOutput);
            }
            catch (JsonException exception)
            {
                throw new IPhoneException(Explain(result.StandardError), exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Serves the device's screen on <c>http://127.0.0.1:port/</c> until
    /// the session is killed, which ends its whole process tree.
    /// </summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task<IProcessSession> StartMirrorAsync(
        IPhoneDevice device, int port, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var python = await EnsureInstalledAsync(cancellationToken).ConfigureAwait(false);

            // Xcode does this on its own: the display service comes with the
            // developer disk image. Does nothing when it is already there.
            await RunAsync(
                ToolRequest(python, device.Udid, "mounter", "auto-mount") with { Timeout = MountTimeout },
                cancellationToken).ConfigureAwait(false);

            LogStarting(device.Name, port);

            try
            {
                return _launcher.Start(ToolRequest(
                    python, device.Udid,
                    "developer", "core-device", "display", "serve-web",
                    "--http-port", port.ToString(CultureInfo.InvariantCulture)));
            }
            catch (ProcessLaunchException exception)
            {
                throw new IPhoneException(exception.Message, exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>A uv command, with uv's Python only and every folder under <paramref name="root"/>.</summary>
    public static ProcessRequest UvRequest(string uvPath, string root, params string[] arguments) =>
        new()
        {
            FileName = uvPath,
            Arguments = arguments,
            Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                // One the player installed would make every machine behave
                // differently.
                ["UV_PYTHON_PREFERENCE"] = "only-managed",
                ["UV_CACHE_DIR"] = Path.Combine(root, "cache"),
                ["UV_PYTHON_INSTALL_DIR"] = Path.Combine(root, "python"),
                ["NO_COLOR"] = "1",
            },
            KillEntireTree = true,
        };

    /// <summary>A pymobiledevice3 command, run by the installed Python.</summary>
    public static ProcessRequest ToolRequest(string python, string? udid, params string[] arguments)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["NO_COLOR"] = "1" };

        // The tool reads --udid itself for its tunnel; the variable reaches
        // every subcommand the same way.
        if (udid is not null)
        {
            environment["PYMOBILEDEVICE3_UDID"] = udid;
        }

        return new ProcessRequest
        {
            FileName = python,
            Arguments = ["-m", "pymobiledevice3", .. arguments],
            Environment = environment,

            // The venv's python.exe is a launcher that starts the real one.
            KillEntireTree = true,
        };
    }

    /// <summary>
    /// What to tell the player, from the tool's error output. Only its last
    /// error line counts: earlier log lines may name pairing or usbmuxd
    /// without being the cause. Its messages are its exceptions' docstrings.
    /// </summary>
    public static string Explain(string error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var line = error.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Contains("ERROR ", StringComparison.Ordinal));
        if (line is null)
        {
            return Strings.Get("IPhoneFailed");
        }

        var message = line[(line.IndexOf("ERROR ", StringComparison.Ordinal) + "ERROR ".Length)..];

        if (message.Contains("usbmuxd", StringComparison.Ordinal))
        {
            return Strings.Get("IPhoneAppleDevicesMissing");
        }

        if (message.Contains("Developer Mode", StringComparison.Ordinal))
        {
            return Strings.Get("IPhoneDeveloperModeOff");
        }

        if (message.Contains("password protected", StringComparison.Ordinal)
            || message.Contains("paired", StringComparison.OrdinalIgnoreCase)
            || message.Contains("pairing", StringComparison.OrdinalIgnoreCase))
        {
            return Strings.Get("IPhoneTrust");
        }

        if (message.Contains("not connected", StringComparison.Ordinal))
        {
            return Strings.Get("IPhoneUnplugged");
        }

        if (message.Contains("Screen streaming needs iOS", StringComparison.Ordinal))
        {
            return Strings.Get("IPhoneNeedsIos27");
        }

        return message;
    }

    /// <summary>
    /// The venv's Python, set up on first need and again whenever the
    /// embedded package list changes.
    /// </summary>
    private async Task<string> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        var venv = Venv;
        var python = Python;
        var stamp = Path.Combine(venv, "dthub-requirements.sha256");

        var requirements = ReadRequirements();
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(requirements)));

        if (File.Exists(python) && File.Exists(stamp)
            && string.Equals(await File.ReadAllTextAsync(stamp, cancellationToken).ConfigureAwait(false), fingerprint, StringComparison.Ordinal))
        {
            return python;
        }

        var uv = await UvAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Root);

            // Without a BOM: the repository's text files carry one.
            var listPath = Path.Combine(Root, "requirements.txt");
            await File.WriteAllTextAsync(listPath, requirements, cancellationToken).ConfigureAwait(false);

            try
            {
                await RunAsync(
                    UvRequest(uv, Root, "venv", "--clear", "--python", PythonVersion, venv) with { Timeout = InstallTimeout },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IPhoneException)
            {
                // A Python missing its files (an antivirus took python.exe on
                // 2026-11-09) fails every try alike: the next one downloads it again.
                try
                {
                    Directory.Delete(Path.Combine(Root, "python"), recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Still held by a process: the next try deletes it.
                }

                throw;
            }
            await RunAsync(
                UvRequest(uv, Root, "pip", "sync", "--require-hashes", "--python", python, listPath) with { Timeout = InstallTimeout },
                cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(stamp, fingerprint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IPhoneException(Strings.Get("IPhoneFailed"), exception);
        }

        return python;
    }

    private static string ReadRequirements()
    {
        using var stream = typeof(PyMobileDevice).Assembly.GetManifestResourceStream(RequirementsResource)
            ?? throw new InvalidOperationException($"Ressource « {RequirementsResource} » absente.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (ProcessLaunchException exception)
        {
            // An antivirus refusing uv or Python lands here.
            throw new IPhoneException(exception.Message, exception);
        }

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

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Affichage de l'iPhone {name} sur le port {port}.")]
    private partial void LogStarting(string name, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{command} a échoué : {error}")]
    private partial void LogFailed(string command, string error);
}
