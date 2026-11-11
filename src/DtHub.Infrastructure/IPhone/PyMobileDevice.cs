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

    /// <summary>
    /// Runs <c>serve-web</c> with three additions. Sound: the tool decodes the
    /// iPhone's AAC-ELD with macOS's AudioToolbox only, so elsewhere PyAV's
    /// FFmpeg, already installed with it, does (checked bit-exact against
    /// FFmpeg's ELD 480 reference on 2026-11-10). Brightness: two more
    /// named buttons, the HID consumer brightness keys, as the screen can
    /// only be dimmed, not switched off, while it is streamed. The game: a
    /// JSON server on <c>127.0.0.1:DTHUB_CONTROL_PORT</c> lists the apps and
    /// launches or stops one through the stream's own tunnel, as a second
    /// tunnel beside it is untried (<see cref="IosGameControl"/>).
    /// </summary>
    public const string MirrorBootstrap = """
        import asyncio, json, os, runpy, sys, urllib.parse
        from pymobiledevice3.remote.core_device import screen_stream
        from pymobiledevice3.remote.core_device.app_service import AppServiceService
        if sys.platform != "darwin":
            import av

            class Decoder:
                def __init__(self, _cookie=None):
                    self.codec = av.CodecContext.create("aac", "r")
                    # The device sends 480-sample units (10 ms); its own cookie says 512.
                    self.codec.extradata = bytes.fromhex("f8e65000")
                    self.codec.sample_rate = 48000
                    self.codec.layout = "stereo"
                    self.resampler = av.AudioResampler(format="s16", layout="stereo", rate=48000)

                def decode(self, au):
                    pcm = b""
                    for frame in self.codec.decode(av.Packet(au)):
                        for out in self.resampler.resample(frame):
                            pcm += bytes(out.planes[0])[: out.samples * 4]
                    return pcm

            screen_stream.AACELDDecoder = Decoder
            screen_stream.ScreenStreamServer._missing_audio_deps = staticmethod(lambda: [])
        screen_stream._NAMED_BUTTONS["brightness-down"] = (0x0C, 0x70, 0.05)
        screen_stream._NAMED_BUTTONS["brightness-up"] = (0x0C, 0x6F, 0.05)


        def file_path(url):
            # CoreDevice encodes a URL as {"relative": "file:///..."}; /private/var and /var are one folder.
            url = url.get("relative", "") if isinstance(url, dict) else str(url or "")
            return urllib.parse.unquote(url.removeprefix("file://")).removeprefix("/private").rstrip("/")


        async def game_pid(apps, bundle):
            # A process names its executable, not its app: it is the one inside the app's folder.
            folder = next((file_path(a.get("url")) + "/" for a in await apps.list_apps() if a.get("bundleIdentifier") == bundle), None)
            if folder is None:
                return None
            processes = await apps.list_processes()
            return next((int(p["processIdentifier"]) for p in processes if file_path(p.get("executableURL")).startswith(folder)), None)


        async def command(apps, method, path, query, body):
            if method == "GET" and path == "/apps":
                return [{"name": str(a.get("name", "")), "bundle": str(a.get("bundleIdentifier", ""))} for a in await apps.list_apps()]
            if method == "GET" and path == "/running":
                return {"pid": await game_pid(apps, query["bundle"][0])}
            if method == "POST" and path == "/launch":
                # Not kill_existing: launching a running game brings it forward instead of restarting it.
                await apps.launch_application(body["bundle"], kill_existing=False)
                return {}
            if method == "POST" and path == "/stop":
                pid = await game_pid(apps, body["bundle"])
                if pid is not None:
                    await apps.send_signal_to_process(pid, 9)
                return {}
            raise ValueError(f"unknown command {method} {path}")


        async def control(server, reader, writer):
            try:
                method, target, *_ = (await reader.readline()).decode("latin-1").split() + ["", ""]
                headers = {}
                while (line := await reader.readline()) not in (b"\r\n", b""):
                    name, _, value = line.decode("latin-1").partition(":")
                    headers[name.strip().lower()] = value.strip()
                # The stream's own check: a web page in the player's browser is turned away.
                refusal = server._refuse_request(headers)
                if refusal is not None:
                    writer.write(refusal)
                    return
                url = urllib.parse.urlsplit(target)
                try:
                    body = json.loads(await server._read_body(reader, headers) or b"{}")
                    # Read per request: the stream rebinds _rsd when the device comes back.
                    async with AppServiceService(server._rsd) as apps:
                        answer = await command(apps, method, url.path, urllib.parse.parse_qs(url.query), body)
                    status = b"200 OK"
                except Exception as e:
                    status, answer = b"500 Internal Server Error", {"error": repr(e)}
                screen_stream._respond(writer, status, json.dumps(answer).encode(), b"application/json")
                await writer.drain()
            finally:
                writer.close()


        serve = screen_stream.ScreenStreamServer.serve


        async def serve_with_control(self):
            try:
                await asyncio.start_server(lambda r, w: control(self, r, w), "127.0.0.1", int(os.environ["DTHUB_CONTROL_PORT"]))
            except OSError:
                # The screen matters more than the game commands.
                screen_stream.logger.warning("DT Hub control server not started", exc_info=True)
            await serve(self)


        screen_stream.ScreenStreamServer.serve = serve_with_control
        runpy.run_module("pymobiledevice3", run_name="__main__", alter_sys=True)
        """;

    private readonly IDependencyProvisioner _provisioner;
    private readonly IProcessRunner _runner;
    private readonly IProcessLauncher _launcher;
    private readonly IAppPaths _paths;
    private readonly ILogger<PyMobileDevice> _logger;

    // One setup at a time, across every window: two first setups would
    // download into the same folders.
    private readonly SemaphoreSlim _installGate = new(1, 1);

    // One mount at a time: they share the downloaded disk image. Apart from the
    // setup, so a mount waiting on the network (up to five minutes) holds no list.
    private readonly SemaphoreSlim _mountGate = new(1, 1);

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
        var python = await InstalledAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Serves the device's screen on <c>http://127.0.0.1:port/</c>, and the game
    /// commands on <paramref name="controlPort"/> (<see cref="IosGameControl"/>), until
    /// the session is killed, which ends its whole process tree.
    /// </summary>
    /// <exception cref="IPhoneException">The message says what to do.</exception>
    public async Task<IProcessSession> StartMirrorAsync(
        IPhoneDevice device, int port, bool audio, int controlPort, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        var python = await InstalledAsync(cancellationToken).ConfigureAwait(false);

        await _mountGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Xcode does this on its own: the display service comes with the
            // developer disk image. Does nothing when it is already there.
            await RunAsync(
                ToolRequest(python, device.Udid, "mounter", "auto-mount") with { Timeout = MountTimeout },
                cancellationToken).ConfigureAwait(false);

            LogStarting(device.Name, port);

            try
            {
                return _launcher.Start(MirrorRequest(python, device.Udid, port, audio, controlPort));
            }
            catch (ProcessLaunchException exception)
            {
                throw new IPhoneException(exception.Message, exception);
            }
        }
        finally
        {
            _mountGate.Release();
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

    /// <summary><c>serve-web</c> through <see cref="MirrorBootstrap"/>, silent when the player turned sound off.</summary>
    public static ProcessRequest MirrorRequest(string python, string udid, int port, bool audio, int controlPort)
    {
        string[] arguments =
        [
            "developer", "core-device", "display", "serve-web",
            "--http-port", port.ToString(CultureInfo.InvariantCulture),
            .. audio ? Array.Empty<string>() : ["--no-audio"],
        ];

        var request = ToolRequest(python, udid, arguments);

        return request with
        {
            Arguments = ["-c", MirrorBootstrap, .. arguments],
            Environment = new Dictionary<string, string?>(request.Environment, StringComparer.Ordinal)
            {
                ["DTHUB_CONTROL_PORT"] = controlPort.ToString(CultureInfo.InvariantCulture),
            },
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
            // The disk image comes from GitHub and Apple signs it: offline, the tool
            // ends on a traceback (wrapped by rich) naming the connection pool.
            return error.Contains("HTTPSConnectionPool(", StringComparison.Ordinal)
                ? Strings.Get("IPhoneNoInternet")
                : Strings.Get("IPhoneFailed");
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

        if (message.Contains("camera or microphone is in use", StringComparison.Ordinal))
        {
            return Strings.Get("IPhoneMediaInUse");
        }

        return message;
    }

    private async Task<string> InstalledAsync(CancellationToken cancellationToken)
    {
        await _installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await EnsureInstalledAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _installGate.Release();
        }
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
            throw new IPhoneException(result.TimedOut ? Strings.Get("IPhoneTimedOut") : Explain(result.StandardError));
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

    public void Dispose()
    {
        _installGate.Dispose();
        _mountGate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Affichage de l'iPhone {name} sur le port {port}.")]
    private partial void LogStarting(string name, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{command} a échoué : {error}")]
    private partial void LogFailed(string command, string error);
}
